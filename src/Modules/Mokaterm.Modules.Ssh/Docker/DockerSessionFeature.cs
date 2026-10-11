using Mokaterm.Abstractions.Commands;
using Mokaterm.Modules.Ssh.Elevation;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// The Docker manager's state for one SSH session, which outlives the dialog it is shown in: what looking for Docker
/// found, and whether commands go through sudo. Reached through <c>GetFeature&lt;DockerSessionFeature&gt;()</c> and
/// disposed with the session, which wipes the sudo password it holds.
/// </summary>
/// <remarks>
/// Public only so another session can offer the manager over its own commands (<see cref="ForCommands"/>), which is how
/// the workbench shows it without a docker host; everything the manager itself uses stays internal.
/// </remarks>
public sealed class DockerSessionFeature : IDisposable
{
	private const int MaxExecHistory = 15;

	private readonly List<DockerExecRequest> _history = [];
	private readonly Func<CancellationToken, Task<SudoRunner>>? _authenticate;
	private readonly TimeProvider _time;
	private readonly SemaphoreSlim _slots = new(DockerClient.MaxConcurrentCommands, DockerClient.MaxConcurrentCommands);
	private readonly SemaphoreSlim _switching = new(1, 1);
	private SudoRunner? _runner;
	private DockerClient _client;
	private DockerEngineStatus? _status;
	private int _disposed;

	/// <param name="userShell">Commands as the login.</param>
	/// <param name="authenticate">Gets sudo going for this session, asking for a password when it needs one; null when the connection does not allow running as root.</param>
	/// <param name="host">The server, for messages.</param>
	/// <param name="userName">The login's user, for the docker group advice.</param>
	internal DockerSessionFeature(
		IDockerShell userShell,
		Func<CancellationToken, Task<SudoRunner>>? authenticate,
		TimeProvider time,
		string host,
		string userName)
	{
		ArgumentNullException.ThrowIfNull(userShell);
		ArgumentNullException.ThrowIfNull(time);
		_authenticate = authenticate;
		_time = time;
		Host = host;
		UserName = userName;
		_client = new DockerClient(userShell, host, _slots);
	}

	/// <summary>Raised when the status or the shell changes. Handlers may run on any thread.</summary>
	public event Action? Changed;

	internal DockerClient Client => Volatile.Read(ref _client);

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
	/// own with sudo; this is for a session that only has commands to offer.
	/// </summary>
	public static DockerSessionFeature ForCommands(IRemoteCommandFeature commands, TimeProvider time, string host, string userName) =>
		new(new UserDockerShell(commands), authenticate: null, time, host, userName);

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
				Volatile.Write(ref _client, new DockerClient(new SudoDockerShell(runner, _time), Host, _slots));
			}
		}
		finally
		{
			_switching.Release();
		}

		return await DetectAsync(cancellationToken);
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		Interlocked.Exchange(ref _runner, null)?.Dispose();
	}
}
