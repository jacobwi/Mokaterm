using System.Globalization;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Containers;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Modules.Ssh.FileSystem;
using Mokaterm.Modules.Ssh.Shell;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// A running container's files through the Docker manager's client: every operation is one of the module's file scripts
/// (<see cref="RemoteScripts"/>) run with <c>docker exec</c>, as the login or through sudo, whichever the manager uses at
/// that moment, and each exec is a channel under the connection's budget. The scripts are POSIX sh with a BusyBox
/// fallback, so Alpine and Debian images read the same, and paths stay positional arguments inside the container.
/// </summary>
/// <remarks>
/// Operations run as the container's own user, the one <c>docker exec</c> picks. A read is a run of bounded pieces
/// (<see cref="PieceReadStream"/>), the same as the root view's. A write is staged on the docker host as the login, over the session's own file
/// system, and the host then pipes that file into <see cref="RemoteScripts.WriteInput"/> with <c>docker exec -i</c>: no exec
/// channel here carries stdin as the login, and writing in place keeps an existing file's owner and mode, which
/// <c>docker cp</c> would hand to root, and works on a single file mounted into the container, where a rename cannot.
/// </remarks>
internal sealed class ContainerFileSystem : ScriptFileSystem
{
	/// <summary>The most a listing may print: past it a folder holds more than one listing carries, and it is refused.</summary>
	internal const int ListingOutputBytes = 32 * 1024 * 1024;

	private const int AccountsOutputBytes = 4 * 1024 * 1024;

	// A piece is at most 4 MB, which a slow link needs a while for. It counts as long all the same, like a root piece: a
	// reader keeps two on their way.
	private static readonly TimeSpan PieceTimeout = TimeSpan.FromMinutes(2);

	private static readonly TimeSpan AccountsTimeout = TimeSpan.FromSeconds(15);

	private readonly Func<DockerClient> _client;
	private readonly DockerHostFiles? _hostFiles;
	private readonly ILogger? _logger;
	private readonly string _home;
	private RemoteAccounts? _accounts;
	private int _disposed;

	private ContainerFileSystem(Func<DockerClient> client, string container, string userName, string home, DockerHostFiles? hostFiles, ILogger? logger)
	{
		_client = client;
		Container = container;
		UserName = userName;
		_home = home;
		_hostFiles = hostFiles;
		_logger = logger;
	}

	/// <summary>The container's name, which every exec goes to.</summary>
	public string Container { get; }

	public override RemoteFileSystemFeatures Features =>
		RemoteFileSystemFeatures.Permissions | RemoteFileSystemFeatures.Ownership | RemoteFileSystemFeatures.SymbolicLinks
		| RemoteFileSystemFeatures.Accounts | RemoteFileSystemFeatures.Copy;

	/// <summary>The container's user as <c>id</c> names it, or its number when the image has no name for it.</summary>
	public override string UserName { get; }

	public override bool IsElevated => false;

	/// <summary>
	/// Asks the container who runs in it and where that user's home is, which is also the first proof it runs and has a
	/// shell: a stopped or distroless container is refused here with a sentence that says so.
	/// </summary>
	/// <param name="client">The manager's client as it is at each call: the sudo switch applies to files too.</param>
	/// <param name="hostFiles">The docker host's files, which uploads are staged through; null leaves the container read-only.</param>
	/// <exception cref="RemoteFileSystemException">The container cannot be reached.</exception>
	public static async Task<ContainerFileSystem> OpenAsync(
		Func<DockerClient> client,
		string container,
		DockerHostFiles? hostFiles,
		ILogger? logger,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(client);
		ArgumentNullException.ThrowIfNull(container);
		ReadOnlyMemory<byte> payload = await ExecuteAsync(client(), container, Call(container, RemoteScripts.Identity, []), RemotePath.Root, cancellationToken);
		List<string> fields = RemoteOutput.SplitFields(payload.Span);
		string user = fields is [{ Length: > 0 } name, ..] ? name : "?";
		string home = fields is [_, { } folder, ..] && folder.StartsWith('/') ? RemotePath.Normalize(folder) : RemotePath.Root;
		return new ContainerFileSystem(client, container, user, home, hostFiles, logger);
	}

	public override ValueTask<string> GetHomeDirectoryAsync(CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		return ValueTask.FromResult(_home);
	}

	public override async ValueTask<string> ResolvePathAsync(string path, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(path);
		return await ResolveAbsoluteAsync(Absolute(SftpFileSystem.ExpandHome(path, _home)), cancellationToken);
	}

	public override async ValueTask<RemoteAccounts> ListAccountsAsync(CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		if (Volatile.Read(ref _accounts) is { } known)
		{
			return known;
		}

		try
		{
			DockerCall call = Call(Container, RemoteScripts.Accounts, []) with { Timeout = AccountsTimeout, OutputLimitBytes = AccountsOutputBytes };
			ReadOnlyMemory<byte> payload = await RunAsync(call, RemotePath.Root, cancellationToken);
			RemoteAccounts accounts = AccountDirectory.FromScript(payload.Span).Accounts;
			Volatile.Write(ref _accounts, accounts);
			return accounts;
		}
		catch (RemoteFileSystemException ex)
		{
			// Names are a nicety for the owner field; without them the field takes what is typed.
			_logger?.LogDebug("A container's users and groups could not be read: {Error}", LogSafe.Describe(ex));
			return RemoteAccounts.Empty;
		}
	}

	public override async ValueTask UploadAsync(string path, Stream source, UploadOptions options, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(options);
		string target = Absolute(path);
		if (_hostFiles is not { } hostFiles)
		{
			throw new RemoteFileSystemException(
				RemoteFileErrorKind.NotSupported,
				"Files reach a container through the docker host's own files, which this session cannot write.",
				target);
		}

		IRemoteFileSystem login = await hostFiles.OpenAsync(asRoot: false, cancellationToken);
		await using StagedUpload staged = StagedUpload.In(login, hostFiles.StagingDirectory, _logger);
		await staged.WriteAsync(source, progress, target, cancellationToken);

		string mode = options.Permissions is { } permissions ? UnixFileModeFormat.ToOctal(permissions) : "";
		string stamp = options.LastModified is { } lastModified
			? lastModified.UtcDateTime.ToString("yyyyMMddHHmm.ss", CultureInfo.InvariantCulture)
			: "";
		DockerCall call = DockerCommands.ExecScriptWithInput(Container, staged.Path, RemoteScripts.WriteInput, [target, Flag(options.Overwrite), mode, stamp])
			with { Timeout = DockerCall.LongTimeout, OutputLimitBytes = PieceSlackBytes };
		await RunAsync(call, target, cancellationToken);
	}

	public override ValueTask DisposeAsync()
	{
		Interlocked.Exchange(ref _disposed, 1);
		return ValueTask.CompletedTask;
	}

	/// <summary>The exec for a script, with the time a script of its kind may take.</summary>
	internal static DockerCall Call(string container, string script, IReadOnlyList<string> arguments)
	{
		TimeSpan timeout = RemoteScripts.RunsLong(script) ? DockerCall.LongTimeout
			: IsChange(script) ? DockerCall.ChangeTimeout
			: DockerCall.DefaultTimeout;
		int output = script == RemoteScripts.List ? ListingOutputBytes : DockerCall.ChangeOutputBytes;
		return DockerCommands.ExecScript(container, script, arguments) with { Timeout = timeout, OutputLimitBytes = output };
	}

	/// <summary>
	/// Runs one exec and returns what the script printed after its marker. Output without the marker means the script
	/// never ran, and the failure is docker's (<see cref="ContainerFileErrors.FromExec"/>); with it, the script's own exit
	/// and stderr say what went wrong.
	/// </summary>
	internal static async Task<ReadOnlyMemory<byte>> ExecuteAsync(
		DockerClient client,
		string container,
		DockerCall call,
		string path,
		CancellationToken cancellationToken,
		string? destination = null,
		bool allowPartialListing = false)
	{
		ArgumentNullException.ThrowIfNull(client);
		ArgumentNullException.ThrowIfNull(call);
		DockerOutput output;
		try
		{
			output = await client.RunAsync(call, cancellationToken);
		}
		catch (DockerCommandException ex)
		{
			throw ContainerFileErrors.FromCommand(ex, path);
		}

		if (!RemoteOutput.TryGetPayload(output.RawOutput, out _))
		{
			throw ContainerFileErrors.FromExec(output, container, client.Host, path);
		}

		if (output.OutputTruncated)
		{
			throw new RemoteFileSystemException(RemoteFileErrorKind.Unknown, $"The container sent more for {path} than one call here keeps, so it was refused.", path);
		}

		return RemoteOutput.Require(output.ExitCode, output.RawOutput, output.Error, path, destination, allowPartialListing);
	}

	protected override Task<ReadOnlyMemory<byte>> RunScriptAsync(
		string script,
		IReadOnlyList<string> arguments,
		string path,
		CancellationToken cancellationToken,
		string? destination = null,
		bool allowPartialListing = false) =>
		RunAsync(Call(Container, script, arguments), path, cancellationToken, destination, allowPartialListing);

	protected override Task<ReadOnlyMemory<byte>> ReadRangeAsync(IReadOnlyList<string> arguments, string path, int maxOutputBytes, CancellationToken cancellationToken) =>
		RunAsync(
			DockerCommands.ExecScript(Container, RemoteScripts.ReadRange, arguments) with { Timeout = PieceTimeout, RunsLong = true, OutputLimitBytes = maxOutputBytes },
			path,
			cancellationToken);

	protected override void ThrowIfDisposed()
	{
		if (Volatile.Read(ref _disposed) != 0)
		{
			throw new RemoteFileSystemException(RemoteFileErrorKind.ConnectionLost, "The session is closed.");
		}
	}

	// A login runs commands through a shell that refuses any other control character in the line, so a name holding one
	// is refused the same way through sudo too.
	protected override string? PathProblem(string path) =>
		path.Any(character => char.IsControl(character) && character is not ('\t' or '\n' or '\r'))
			? $"{RemotePath.GetName(path)} has a control character in its name, which cannot be passed to docker."
			: null;

	private static bool IsChange(string script) =>
		script == RemoteScripts.Delete
		|| script == RemoteScripts.Rename
		|| script == RemoteScripts.ChangeMode
		|| script == RemoteScripts.ChangeOwner
		|| script == RemoteScripts.ChangeGroup;

	private Task<ReadOnlyMemory<byte>> RunAsync(DockerCall call, string path, CancellationToken cancellationToken, string? destination = null, bool allowPartialListing = false)
	{
		ThrowIfDisposed();
		return ExecuteAsync(_client(), Container, call, path, cancellationToken, destination, allowPartialListing);
	}
}
