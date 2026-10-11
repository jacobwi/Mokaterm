using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Modules.Local.Pty;
using Mokaterm.Modules.Local.Sessions;
using Mokaterm.Modules.Local.Shells;

namespace Mokaterm.Modules.Local;

/// <summary>Opens local shell sessions: the shell, its pseudo console and the terminal on it all run here.</summary>
internal sealed class LocalProtocolProvider : IProtocolProvider, IQuickConnectSuggestions
{
	public const string ProtocolId = "local";

	/// <summary>
	/// A shell on this computer has nothing a port number could mean. The descriptor has to name one, so it names 0, and
	/// <see cref="ProtocolDescriptor.UsesPort"/> is what keeps the number out of the editor and out of every label.
	/// </summary>
	public const int NoPort = 0;

	private readonly ISettingsService _settings;
	private readonly LocalSessionStarter _starter;

	public LocalProtocolProvider(ISettingsService settings, ILoggerFactory loggerFactory)
	{
		_settings = settings;
		_starter = new LocalSessionStarter(loggerFactory);
	}

	public ProtocolDescriptor Descriptor { get; } = new()
	{
		Id = ProtocolId,
		DisplayName = "Local shell",
		Description = "A shell on this computer. The machine's address names the shell, such as pwsh, cmd or a WSL distribution like Ubuntu, and localhost runs the default one.",
		QuickConnectExample = "pwsh, cmd, wsl or a distro",
		DefaultPort = NoPort,
		UsesPort = false,
		Capabilities = ProtocolCapabilities.Terminal,

		// There is no login: the shell runs as the user running this app. A program that wants one asks inside the
		// terminal, and the answer is typed there.
		AuthenticationMethods = [AuthenticationMethod.Anonymous],
		RequiresUsername = false,
		Order = 70,
	};

	/// <summary>
	/// The shells detection finds, by the address that names each one. Detection reads files and the registry and never
	/// starts a program, which is what lets the quick connect box ask every time it opens.
	/// </summary>
	public IReadOnlyList<QuickConnectSuggestion> GetQuickConnectSuggestions() =>
		PseudoTerminalPlatform.IsSupported ? Suggest(LocalShellDetector.Detect(LocalMachine.Current())) : [];

	/// <summary>A WSL distribution's address is its own name, so it says what it is instead of repeating that.</summary>
	internal static IReadOnlyList<QuickConnectSuggestion> Suggest(IReadOnlyList<LocalShell> shells) =>
	[
		.. shells.Select(shell => new QuickConnectSuggestion(
			shell.Address,
			shell.Kind == LocalShellKind.Wsl ? "WSL distribution" : shell.DisplayName)),
	];

	public async Task<IProtocolSession> ConnectAsync(ProtocolConnectContext context, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(context);
		if (!PseudoTerminalPlatform.IsSupported)
		{
			throw new ProtocolConnectException(ConnectFailure.HostUnreachable, PseudoTerminalPlatform.Unsupported);
		}

		LocalSettings settings = _settings.Get<LocalSettings>().Clamped();
		LocalConnectionOptions options = LocalConnectionOptions.From(context.Connection.Options);
		LocalMachine machine = LocalMachine.Current();
		IReadOnlyList<LocalShell> shells = LocalShellDetector.Detect(machine);
		if (!LocalShellResolver.TryResolve(options, context.Host.Address, settings, shells, machine, out LocalShell? shell, out string? problem)
			|| !LocalLaunchBuilder.TryBuild(shell, options, settings, machine, LocalSessionStarter.TerminalType(_settings), out PseudoTerminalLaunch? launch, out problem))
		{
			throw new ProtocolConnectException(ConnectFailure.HostUnreachable, problem);
		}

		context.Status?.Report($"Starting {shell.DisplayName}");
		return await _starter.StartAsync(
			context.SessionId,
			launch,
			shell.DisplayName,
			shell.Kind.ToString(),
			settings.ReportExitCode,
			context.TerminalSize,
			cancellationToken);
	}
}
