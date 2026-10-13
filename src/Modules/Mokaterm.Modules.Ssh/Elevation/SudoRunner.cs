using System.Security.Cryptography;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Modules.Ssh.FileSystem;
using Mokaterm.Modules.Ssh.Sessions;
using Mokaterm.Modules.Ssh.Shell;
using Renci.SshNet;
using LoginMethod = Mokaterm.Abstractions.Connections.AuthenticationMethod;

namespace Mokaterm.Modules.Ssh.Elevation;

/// <summary>Everything needed to get sudo to run commands for one elevated file system.</summary>
internal sealed record SudoLogin
{
	public required SshClient Client { get; init; }

	/// <summary>The sudo executable, validated as a single command word.</summary>
	public required string SudoCommand { get; init; }

	/// <summary><c>user@host</c>, for prompts and messages.</summary>
	public required string Account { get; init; }

	/// <summary>The session's login password, tried once without asking when the login used one. Not owned.</summary>
	public SecretBuffer? LoginPassword { get; init; }

	public required SudoPasswordCache Cache { get; init; }

	public required IUserInteraction Interaction { get; init; }

	/// <summary>Show "Remember for this session" on the password prompt.</summary>
	public required bool OfferRemember { get; init; }

	/// <summary>
	/// The session's own sudo: the sudo command from the settings, the login password when the login used one, and
	/// the password the user chose to remember for this session.
	/// </summary>
	public static SudoLogin ForSession(ISshSessionHost host, SshClient client)
	{
		ArgumentNullException.ThrowIfNull(host);
		ArgumentNullException.ThrowIfNull(client);
		return new SudoLogin
		{
			Client = client,
			SudoCommand = host.Connector.Settings.EffectiveSudoCommand,
			Account = host.Context.Account,
			LoginPassword = host.Context.Credentials.Method is LoginMethod.Password or LoginMethod.KeyboardInteractive
				? host.Context.Credentials.Password
				: null,
			Cache = host.SudoPasswords,
			Interaction = host.Context.Interaction,
			OfferRemember = host.Connector.SettingsService.Get<SecuritySettings>().RememberSudoPasswordForSession,
		};
	}
}

/// <summary>
/// Runs scripts as root through <c>sudo -S</c>. The password only ever travels on stdin, never on a command line, and the
/// scripts get their paths as positional arguments.
/// </summary>
internal sealed class SudoRunner : IDisposable
{
	private const int MaxPasswordPrompts = 3;

	private readonly Func<string, SecretBuffer?, bool, CancellationToken, Task<IExecChannel>> _start;
	private readonly string _sudoCommand;
	private SecretBuffer? _password;

	/// <param name="start">
	/// Starts a command line, writes the input it is given (the password, then any input for the script) on stdin and
	/// closes stdin, also when there is no input; the bool is the channel's <c>runsLong</c>. The connection's own is
	/// <see cref="RemoteCommand.StartAsync"/>; tests run the commands in a local shell instead.
	/// </param>
	/// <param name="password">The password sudo took, written ahead of every command from now on; owned. Null when sudo needs none.</param>
	internal SudoRunner(Func<string, SecretBuffer?, bool, CancellationToken, Task<IExecChannel>> start, string sudoCommand, string account, SecretBuffer? password)
	{
		_start = start;
		_sudoCommand = sudoCommand;
		Account = account;
		_password = password;
	}

	private SudoRunner(SshClient client, string sudoCommand, string account, SecretBuffer? password)
		: this((command, input, runsLong, token) => StartOnAsync(client, command, input, runsLong, token), sudoCommand, account, password)
	{
	}

	public string Account { get; }

	/// <summary>
	/// Finds a way to run as root: without a password when sudo allows it, else a remembered password, the login password,
	/// then asking the user.
	/// </summary>
	/// <exception cref="RemoteFileSystemException">sudo refused (<see cref="RemoteFileErrorKind.ElevationFailed"/>) or the connection failed.</exception>
	public static async Task<SudoRunner> AuthenticateAsync(SudoLogin login, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(login);
		SudoCheck probe = await CheckAsync(login, password: null, cancellationToken);
		if (probe.Succeeded)
		{
			return new SudoRunner(login.Client, login.SudoCommand, login.Account, password: null);
		}

		if (probe.Failure is not (SudoFailure.PasswordRequired or SudoFailure.IncorrectPassword))
		{
			throw Refused(probe, login.Account);
		}

		if (login.Cache.CopyPassword() is { } remembered)
		{
			if (await TryPasswordAsync(login, remembered, cancellationToken) is { } runner)
			{
				return runner;
			}

			login.Cache.Forget();
		}

		if (login.LoginPassword is { } loginPassword && await TryPasswordAsync(login, loginPassword.Copy(), cancellationToken) is { } withLoginPassword)
		{
			return withLoginPassword;
		}

		string? message = null;
		for (int prompt = 0; prompt < MaxPasswordPrompts; prompt++)
		{
			SecretPromptResult answer = await login.Interaction.PromptSecretAsync(
				new SecretPrompt
				{
					Title = $"sudo password for {login.Account}",
					Message = message,
					Label = "Password",
					OfferRemember = login.OfferRemember,
				},
				cancellationToken) ?? throw new RemoteFileSystemException(RemoteFileErrorKind.ElevationFailed, "Running as root was cancelled.");

			using SecretBuffer typed = SecretBuffer.FromString(answer.Secret);
			if (await TryPasswordAsync(login, typed.Copy(), cancellationToken) is { } withTypedPassword)
			{
				if (answer.Remember && login.OfferRemember)
				{
					login.Cache.Remember(typed.Copy());
				}

				return withTypedPassword;
			}

			message = "Sorry, try again.";
		}

		throw new RemoteFileSystemException(
			RemoteFileErrorKind.ElevationFailed,
			SudoErrors.Describe(SudoFailure.IncorrectPassword, login.Account, ""));
	}

	/// <summary><c>LC_ALL=C sudo -S -p '' -- sh -c SCRIPT sh ARG...</c>, or <c>-n</c> instead of <c>-S -p ''</c> without a password.</summary>
	internal static string BuildCommand(string sudoCommand, bool withPassword, string script, IEnumerable<string> arguments)
	{
		// C locale keeps sudo's own messages in English so they can be classified.
		string sudo = $"LC_ALL=C {PosixShell.Quote(sudoCommand)} {(withPassword ? "-S -p ''" : "-n")} -- ";
		return sudo + PosixShell.ScriptCommand(RemoteScripts.ElevatedPreamble + script, arguments);
	}

	/// <summary>
	/// Returns the script's output after the marker, or throws: <see cref="RemoteFileErrorKind.ElevationFailed"/> when sudo
	/// never ran the script, otherwise the error the script's exit status and stderr describe.
	/// </summary>
	internal static ReadOnlyMemory<byte> EnsureSucceeded(RemoteCommandResult result, string account, string path, string? destination = null, bool allowPartialListing = false)
	{
		EnsureElevated(result.ExitStatus, result.Error, account, path, out string scriptError);
		return RemoteOutput.Require(result.ExitStatus, result.Output, scriptError, path, destination, allowPartialListing);
	}

	/// <summary>Throws <see cref="RemoteFileErrorKind.ElevationFailed"/> unless stderr shows sudo ran the script.</summary>
	internal static void EnsureElevated(int? exitStatus, string error, string account, string path, out string scriptError)
	{
		(bool elevated, string sudoOutput, string afterMarker) = SudoErrors.Split(error);
		if (!elevated)
		{
			SudoFailure failure = SudoErrors.Classify(sudoOutput, exitStatus);
			throw new RemoteFileSystemException(RemoteFileErrorKind.ElevationFailed, SudoErrors.Describe(failure, account, sudoOutput), path);
		}

		scriptError = afterMarker;
	}

	/// <summary>Runs a script as root to completion.</summary>
	/// <param name="runsLong">The script may take minutes, as a copy or an archive can (<see cref="ExecChannelBudget"/>).</param>
	public Task<RemoteCommandResult> RunAsync(string script, IEnumerable<string> arguments, bool runsLong, CancellationToken cancellationToken) =>
		RunAsync(script, arguments, runsLong, RemoteCommand.MaxOutputBytes, cancellationToken);

	/// <summary>
	/// Runs a script as root to completion, refusing stdout past <paramref name="maxOutputBytes"/>. Every command gets
	/// the password sudo took when this runner was made, the same way: nothing here ever asks again.
	/// </summary>
	/// <exception cref="RemoteFileSystemException">The script wrote more than <paramref name="maxOutputBytes"/>; it is stopped.</exception>
	public async Task<RemoteCommandResult> RunAsync(string script, IEnumerable<string> arguments, bool runsLong, int maxOutputBytes, CancellationToken cancellationToken)
	{
		IExecChannel channel = await _start(BuildCommand(_sudoCommand, _password is not null, script, arguments), _password, runsLong, cancellationToken);
		return await CommandExecution.ReadToEndAsync(channel, maxOutputBytes, cancellationToken);
	}

	/// <summary>
	/// Runs a script as root the way a command run does (<see cref="CommandExecution"/>): stdout and stderr read side by
	/// side and each kept up to the request's cap, so a chatty command still reports how it ended, and cancelling stops
	/// it. stderr starts with whatever sudo printed, up to <see cref="RemoteScripts.ElevatedMarker"/>;
	/// <see cref="SudoErrors.Split"/> takes it apart. <paramref name="input"/> follows the password line on stdin; with
	/// neither, stdin closes at once so a command that reads it ends. The caller keeps <paramref name="input"/>.
	/// </summary>
	public async Task<RemoteCommandOutcome> ExecuteAsync(
		string script,
		IEnumerable<string> arguments,
		SecretBuffer? input,
		RemoteCommandRequest request,
		TimeProvider time,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		string command = BuildCommand(_sudoCommand, _password is not null, script, arguments);
		return await CommandExecution.RunAsync(token => StartExecutionAsync(command, input, request.RunsLong, token), request, time, cancellationToken);
	}

	public void Dispose() => Interlocked.Exchange(ref _password, null)?.Dispose();

	private static async Task<IExecChannel> StartOnAsync(SshClient client, string command, SecretBuffer? input, bool runsLong, CancellationToken cancellationToken)
	{
		RemoteCommand started = await RemoteCommand.StartAsync(client, command, input, runsLong, cancellationToken);
		if (input is null)
		{
			// Nothing was written, and SSH.NET sends EOF only after data, so stdin has to be closed by hand.
			started.CloseInput();
		}

		return started;
	}

	private async Task<IExecChannel> StartExecutionAsync(string command, SecretBuffer? input, bool runsLong, CancellationToken cancellationToken)
	{
		using SecretBuffer? stdin = JoinInput(input);
		return await _start(command, stdin, runsLong, cancellationToken);
	}

	/// <summary>sudo -S reads the first line; the rest of stdin belongs to the script.</summary>
	private SecretBuffer? JoinInput(SecretBuffer? input)
	{
		SecretBuffer? password = _password;
		if (password is null || input is null)
		{
			return (password ?? input)?.Copy();
		}

		byte[] joined = GC.AllocateArray<byte>(password.Length + 1 + input.Length, pinned: true);
		try
		{
			password.Span.CopyTo(joined);
			joined[password.Length] = (byte)'\n';
			input.Span.CopyTo(joined.AsSpan(password.Length + 1));
			return SecretBuffer.FromBytes(joined);
		}
		finally
		{
			CryptographicOperations.ZeroMemory(joined);
		}
	}

	private static async Task<SudoRunner?> TryPasswordAsync(SudoLogin login, SecretBuffer password, CancellationToken cancellationToken)
	{
		SudoCheck check;
		try
		{
			check = await CheckAsync(login, password, cancellationToken);
		}
		catch
		{
			password.Dispose();
			throw;
		}

		if (check.Succeeded)
		{
			return new SudoRunner(login.Client, login.SudoCommand, login.Account, password);
		}

		password.Dispose();
		if (check.Failure != SudoFailure.IncorrectPassword)
		{
			throw Refused(check, login.Account);
		}

		return null;
	}

	private static async Task<SudoCheck> CheckAsync(SudoLogin login, SecretBuffer? password, CancellationToken cancellationToken)
	{
		RemoteCommandResult result;
		try
		{
			result = await RemoteCommand.RunAsync(login.Client, BuildCommand(login.SudoCommand, password is not null, "", []), password, runsLong: false, cancellationToken);
		}
		catch (Exception ex) when (RemoteFileErrors.TryMap(ex, null, out RemoteFileSystemException? mapped))
		{
			throw mapped;
		}

		(bool elevated, string sudoOutput, _) = SudoErrors.Split(result.Error);
		return elevated && result.ExitStatus == 0
			? new SudoCheck(true, SudoFailure.Other, "")
			: new SudoCheck(false, SudoErrors.Classify(sudoOutput, result.ExitStatus), sudoOutput);
	}

	private static RemoteFileSystemException Refused(SudoCheck check, string account) =>
		new(RemoteFileErrorKind.ElevationFailed, SudoErrors.Describe(check.Failure, account, check.Error));

	private readonly record struct SudoCheck(bool Succeeded, SudoFailure Failure, string Error);
}
