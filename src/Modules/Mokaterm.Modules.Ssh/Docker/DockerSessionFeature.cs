using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Containers;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Modules.Ssh.Elevation;
using Mokaterm.Modules.Ssh.FileSystem;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// The Docker manager's state for one SSH session, which outlives the dialog it is shown in: what looking for Docker
/// found, whether commands go through sudo, and the docker host's files. Reached through
/// <c>GetFeature&lt;DockerSessionFeature&gt;()</c> and disposed with the session, which wipes the sudo password it holds.
/// The root file system it reads and edits through is the session's, shared with the system view.
/// </summary>
/// <remarks>
/// Public only so another session can offer the manager over its own commands (<see cref="ForCommands"/>), which is how
/// the workbench shows it without a docker host; everything the manager itself uses stays internal.
/// </remarks>
public sealed class DockerSessionFeature : IAsyncDisposable
{
	private const int MaxExecHistory = 15;

	private readonly List<DockerExecRequest> _history = [];
	private readonly Func<CancellationToken, Task<SudoRunner>>? _authenticate;
	private readonly TimeProvider _time;
	private readonly SemaphoreSlim _switching = new(1, 1);
	private readonly Dictionary<string, ContainerFileSystemFeature> _containerFiles = new(StringComparer.Ordinal);
	private readonly ILogger? _logger;
	private SudoRunner? _runner;
	private DockerClient _client;
	private DockerEngineStatus? _status;
	private DockerClientConfig? _clientConfig;
	private BuilderFound? _builder;
	private int _disposed;

	/// <param name="userShell">Commands as the login.</param>
	/// <param name="authenticate">Gets sudo going for this session, asking for a password when it needs one; null when the connection does not allow running as root.</param>
	/// <param name="host">The server, for messages.</param>
	/// <param name="userName">The login's user, for the docker group advice.</param>
	/// <param name="files">The session's files, for the env and compose files and volume folders; null when it has none.</param>
	/// <param name="stagingDirectory">Where uploads into containers wait on the docker host; null for <c>/tmp</c>.</param>
	/// <param name="logger">Hears about staging files that could not be removed.</param>
	/// <param name="rootFiles">The session's one root file system over <paramref name="files"/>, which the session owns; null makes one of the manager's own.</param>
	internal DockerSessionFeature(
		IDockerShell userShell,
		Func<CancellationToken, Task<SudoRunner>>? authenticate,
		TimeProvider time,
		string host,
		string userName,
		IFileSystemFeature? files,
		Func<string>? stagingDirectory = null,
		ILogger? logger = null,
		SessionRootFiles? rootFiles = null)
	{
		ArgumentNullException.ThrowIfNull(userShell);
		ArgumentNullException.ThrowIfNull(time);
		_authenticate = authenticate;
		_time = time;
		Host = host;
		UserName = userName;
		_client = new DockerClient(userShell, host);
		_logger = logger;
		HostFiles = rootFiles is not null
			? new DockerHostFiles(rootFiles, stagingDirectory)
			: files is null ? null : new DockerHostFiles(files, stagingDirectory);
		ImageUpdates = new DockerImageUpdates(time);
		Changes = new DockerChangeLog(time);
		LiveRuns = new DockerLiveRuns(time, Changes, logger);
	}

	/// <summary>Raised when the status or the shell changes. Handlers may run on any thread.</summary>
	public event Action? Changed;

	internal DockerClient Client => Volatile.Read(ref _client);

	/// <summary>The docker host's files, or null for a session without a file system.</summary>
	internal DockerHostFiles? HostFiles { get; }

	/// <summary>The update checks run this session, which every page reads to mark what has a newer image.</summary>
	internal DockerImageUpdates ImageUpdates { get; }

	/// <summary>The changes the user ran this session, so the health watch never reports them.</summary>
	internal DockerChangeLog Changes { get; }

	internal DockerRestartLoops RestartLoops { get; } = new();

	/// <summary>The session's builds and pushes, which run on whatever page is shown.</summary>
	internal DockerLiveRuns LiveRuns { get; }

	/// <summary>The registries the client config named at its last read this session, or null before one.</summary>
	internal DockerClientConfig? ClientConfig => Volatile.Read(ref _clientConfig);

	/// <summary>What the last look found, or null before the first one.</summary>
	internal DockerEngineStatus? Status => Volatile.Read(ref _status);

	public string Host { get; }

	public string UserName { get; }

	/// <summary>False when the connection is set never to run anything as root.</summary>
	public bool CanElevate => _authenticate is not null;

	public bool IsElevated => Client.IsElevated;

	/// <summary>Which compose to use, as picked in the manager; kept for the session and never saved.</summary>
	public ComposePreference ComposePreference { get; private set; }

	/// <summary>The compose every compose command is built from: the preference applied to what was found.</summary>
	internal ComposeTool? Compose => Status?.Compose.Pick(ComposePreference);

	/// <summary>The commands run inside containers this session, newest first. In memory only: a command can carry a token.</summary>
	internal IReadOnlyList<DockerExecRequest> ExecHistory
	{
		get
		{
			lock (_history)
			{
				return [.. _history];
			}
		}
	}

	public void SetComposePreference(ComposePreference preference)
	{
		if (ComposePreference != preference)
		{
			ComposePreference = preference;
			Changed?.Invoke();
		}
	}

	/// <summary>Keeps <paramref name="request"/> at the top of the history, once, dropping the oldest past the limit.</summary>
	internal void RememberExec(DockerExecRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		lock (_history)
		{
			_history.RemoveAll(entry => entry.Container == request.Container
				&& entry.Command == request.Command
				&& entry.Shell == request.Shell
				&& entry.User == request.User
				&& entry.WorkingDirectory == request.WorkingDirectory
				&& entry.Environment.SequenceEqual(request.Environment));
			_history.Insert(0, request);
			if (_history.Count > MaxExecHistory)
			{
				_history.RemoveAt(_history.Count - 1);
			}
		}
	}

	/// <summary>
	/// A manager over any session's command feature, as the login and never through sudo. The SSH session builds its
	/// own with sudo; this is for a session that only has commands to offer, and the files it may have beside them.
	/// </summary>
	public static DockerSessionFeature ForCommands(
		IRemoteCommandFeature commands,
		TimeProvider time,
		string host,
		string userName,
		IFileSystemFeature? files = null) =>
		new(new UserDockerShell(commands), authenticate: null, time, host, userName, files);

	/// <summary>
	/// The files of the running container named <paramref name="container"/>, through this manager's client as the login or
	/// through sudo, whichever it uses at each call. One per name for the session, so every browser and transfer of that
	/// container shares it; the session disposes it.
	/// </summary>
	/// <exception cref="ArgumentException">The name is not a container name.</exception>
	internal IFileSystemFeature ContainerFiles(string container)
	{
		ArgumentNullException.ThrowIfNull(container);
		string trimmed = container.Trim();
		string name = DockerCommands.Checked(trimmed, DockerNames.ContainerProblem(trimmed));
		lock (_containerFiles)
		{
			// Checked under the lock the dispose empties the table under, so nothing is added after it.
			ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
			if (!_containerFiles.TryGetValue(name, out ContainerFileSystemFeature? files))
			{
				files = new ContainerFileSystemFeature(() => Client, name, HostFiles, _logger);
				_containerFiles.Add(name, files);
			}

			return files;
		}
	}

	/// <summary>
	/// BuildKit when <c>docker buildx version</c> answers, the classic builder otherwise: asked once per session, and again
	/// after a switch to sudo, since buildx is a plugin each account can have or not.
	/// </summary>
	internal async Task<DockerBuilder> BuilderAsync(CancellationToken cancellationToken)
	{
		DockerClient client = Client;
		if (Volatile.Read(ref _builder) is { } known && ReferenceEquals(known.Client, client))
		{
			return known.Builder;
		}

		DockerBuilder builder;
		try
		{
			builder = (await client.RunAsync(DockerCommands.BuildxVersion(), cancellationToken)).Succeeded ? DockerBuilder.BuildKit : DockerBuilder.Legacy;
		}
		catch (DockerCommandException)
		{
			builder = DockerBuilder.Legacy;
		}

		Volatile.Write(ref _builder, new BuilderFound(client, builder));
		return builder;
	}

	/// <summary>Reads which registries the client is logged in to, through the current shell, and keeps it for the push form.</summary>
	/// <exception cref="DockerCommandException">The file could not be read.</exception>
	internal async Task<DockerClientConfig> ReadClientConfigAsync(CancellationToken cancellationToken)
	{
		DockerClient client = Client;
		DockerOutput output = await client.RunAsync(DockerRegistryCommands.ReadClientConfig(), cancellationToken);
		if (!output.Succeeded)
		{
			// The bytes are wiped whatever the exit, since a file cut short can still hold part of a credential.
			Wipe(output);
			throw client.Failed(output);
		}

		DockerClientConfig config = DockerClientConfigReader.Read(output);
		Volatile.Write(ref _clientConfig, config);
		return config;
	}

	/// <summary>
	/// Starts a build or a push in the background through <paramref name="client"/>, the client whose commands the
	/// question showed, and drops the update checks of what it rebuilt or pushed once it succeeded.
	/// </summary>
	/// <exception cref="InvalidOperationException">Another build or push is still running.</exception>
	internal DockerLiveRun StartLive(DockerLivePlan plan, DockerClient client)
	{
		ArgumentNullException.ThrowIfNull(plan);
		ArgumentNullException.ThrowIfNull(client);
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
		IReadOnlyList<string> shown = Shown(client, plan.Calls);
		return LiveRuns.Start(client, plan, shown, plan.Refreshes.Count == 0 ? null : () => ImageUpdates.Forget(plan.Refreshes));
	}

	/// <summary>Each call as a question shows it, with <c>sudo</c> in front while the manager runs through it.</summary>
	internal static IReadOnlyList<string> Shown(DockerClient client, IReadOnlyList<DockerCall> calls)
	{
		ArgumentNullException.ThrowIfNull(client);
		ArgumentNullException.ThrowIfNull(calls);
		string prefix = client.IsElevated ? "sudo " : "";
		return [.. calls.Select(call => prefix + call.Display)];
	}

	private static void Wipe(DockerOutput output) =>
		System.Security.Cryptography.CryptographicOperations.ZeroMemory(System.Runtime.InteropServices.MemoryMarshal.AsMemory(output.RawOutput).Span);

	/// <summary>Looks for Docker again with the current shell and keeps what it found.</summary>
	internal async Task<DockerEngineStatus> DetectAsync(CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
		DockerEngineStatus status = await DockerDetector.DetectAsync(Client, cancellationToken);
		Volatile.Write(ref _status, status);
		Changed?.Invoke();
		return status;
	}

	/// <summary>
	/// Switches every later command to sudo, which may ask for the sudo password, and looks for Docker again through it.
	/// </summary>
	/// <exception cref="Mokaterm.Abstractions.FileSystem.RemoteFileSystemException">sudo refused, or the prompt was cancelled.</exception>
	internal async Task<DockerEngineStatus> ElevateAsync(CancellationToken cancellationToken)
	{
		if (_authenticate is not { } authenticate)
		{
			throw new InvalidOperationException("This connection does not run anything as root.");
		}

		await _switching.WaitAsync(cancellationToken);
		try
		{
			ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
			if (!Client.IsElevated)
			{
				SudoRunner runner = await authenticate(cancellationToken);
				if (Volatile.Read(ref _disposed) != 0)
				{
					runner.Dispose();
					ObjectDisposedException.ThrowIf(true, this);
				}

				Interlocked.Exchange(ref _runner, runner)?.Dispose();
				Volatile.Write(ref _client, new DockerClient(new SudoDockerShell(runner, _time), Host));
			}
		}
		finally
		{
			_switching.Release();
		}

		return await DetectAsync(cancellationToken);
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		// A build still going is stopped before the sudo password it may run with is wiped.
		await LiveRuns.DisposeAsync();
		Interlocked.Exchange(ref _runner, null)?.Dispose();
		ContainerFileSystemFeature[] containers;
		lock (_containerFiles)
		{
			containers = [.. _containerFiles.Values];
			_containerFiles.Clear();
		}

		foreach (ContainerFileSystemFeature container in containers)
		{
			await container.DisposeAsync();
		}

		if (HostFiles is { } files)
		{
			await files.DisposeAsync();
		}
	}

	private sealed record BuilderFound(DockerClient Client, DockerBuilder Builder);
}
