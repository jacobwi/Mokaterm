using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Ssh.Connection;
using Mokaterm.Modules.Ssh.Elevation;
using Mokaterm.Modules.Ssh.FileSystem;
using Mokaterm.Modules.Ssh.KeepAlive;
using Renci.SshNet;

namespace Mokaterm.Modules.Ssh.Sessions;

/// <summary>
/// A file-only session over SFTP. Running as root opens a companion SSH connection with the same login and trusted host key.
/// </summary>
internal sealed class SftpSession : IProtocolSession, ISshSessionHost
{
	private readonly SftpClient _client;
	private readonly CompanionConnection<SshClient> _ssh;
	private readonly SshFileSystemFeature _fileSystem;
	private readonly ConnectionKeepAlive _keepAlive;
	private readonly ConnectionMonitor _monitor;
	private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly ILogger _logger;
	private readonly Lazy<Task> _teardown;
	private int _disposed;

	/// <summary>Takes ownership of <paramref name="connection"/>.</summary>
	public SftpSession(SshConnector connector, SshConnection<SftpClient> connection, ILogger logger)
	{
		ArgumentNullException.ThrowIfNull(connector);
		ArgumentNullException.ThrowIfNull(connection);
		Connector = connector;
		Context = connection.Context;
		_client = connection.Client;
		_logger = logger;
		_teardown = new Lazy<Task>(TearDownAsync, LazyThreadSafetyMode.ExecutionAndPublication);
		AccountNames = new RemoteAccountNames(() => ConnectedSshClient, logger);
		SudoPasswords = new SudoPasswordCache();

		// As for a terminal session, the jump hosts are covered by this connection's own keepalives.
		SshSettings settings = connector.Settings;
		KeepAlivePolicy? keepAlive = settings.GetKeepAlivePolicy(Context.Options);
		_keepAlive = ConnectionKeepAlive.Open(
			_client,
			keepAlive,
			settings.ConnectTimeout,
			connector.Time,
			notAnswering: () => _logger.LogInformation("The server of SFTP session {SessionId} does not answer keepalive requests, so the session is never dropped for missing them", Context.SessionId));
		if (_keepAlive.Problem is { } problem)
		{
			_logger.LogInformation("SFTP session {SessionId} cannot tell when the server stops answering: {Problem}", Context.SessionId, problem);
		}

		_ssh = new CompanionConnection<SshClient>(
			cancellationToken => connector.ConnectCompanionAsync(Context, static info => new SshClient(info), cancellationToken),
			client => keepAlive is { IsOn: true }
				? ConnectionKeepAlive.Open(
					client,
					keepAlive,
					settings.ConnectTimeout,
					connector.Time,
					notAnswering: () => _logger.LogInformation("The sudo connection of SFTP session {SessionId} does not answer keepalive requests, so it is never closed for missing them", Context.SessionId))
				: null,
			cause => _logger.LogInformation("The sudo connection of SFTP session {SessionId} stopped answering and was closed: {Cause}", Context.SessionId, LogSafe.Describe(cause)));
		_fileSystem = new SshFileSystemFeature(this, logger);
		_monitor = new ConnectionMonitor(_client, _keepAlive.Lost);
		_ = WatchAsync();
	}

	public Task Completion => _completion.Task;

	public SshSessionContext Context { get; }

	public SshConnector Connector { get; }

	public RemoteAccountNames AccountNames { get; }

	public SudoPasswordCache SudoPasswords { get; }

	/// <summary>Only the companion opened for sudo; an SFTP session never opens one just to read account names.</summary>
	public SshClient? ConnectedSshClient => _ssh.Current;

	public TFeature? GetFeature<TFeature>()
		where TFeature : class =>
		typeof(TFeature) == typeof(IFileSystemFeature) ? (TFeature)(object)_fileSystem : null;

	public Task<SftpClient> GetSftpClientAsync(CancellationToken cancellationToken) =>
		Volatile.Read(ref _disposed) == 0 && ConnectionMonitor.IsConnected(_client)
			? Task.FromResult(_client)
			: Task.FromException<SftpClient>(new RemoteFileSystemException(RemoteFileErrorKind.ConnectionLost, "The SFTP connection is closed."));

	public async Task<SshClient> GetSshClientAsync(CancellationToken cancellationToken)
	{
		try
		{
			return await _ssh.GetAsync(cancellationToken);
		}
		catch (ProtocolConnectException ex)
		{
			throw new RemoteFileSystemException(RemoteFileErrorKind.ElevationFailed, $"The SSH connection for sudo could not be opened: {ex.Message}", null, ex);
		}
		catch (ObjectDisposedException ex)
		{
			throw new RemoteFileSystemException(RemoteFileErrorKind.ConnectionLost, "The session is closed.", null, ex);
		}
	}

	/// <summary>Closes every part. A second call waits for the same teardown, like <see cref="SshSession.DisposeAsync"/>.</summary>
	public ValueTask DisposeAsync() => new(_teardown.Value);

	private async Task TearDownAsync()
	{
		Volatile.Write(ref _disposed, 1);
		_completion.TrySetResult();

		// Each part closes even when one before it fails: the connection, the jump hosts and the login must not outlive the session.
		Guid id = Context.SessionId;
		await SessionTeardown.ReleaseAsync(_monitor.DisposeAsync, _logger, id, "the connection monitor");
		await SessionTeardown.ReleaseAsync(_keepAlive.DisposeAsync, _logger, id, "the keepalive");
		await SessionTeardown.ReleaseAsync(_fileSystem.DisposeAsync, _logger, id, "its file systems");
		await SessionTeardown.ReleaseAsync(_ssh.DisposeAsync, _logger, id, "the SSH connection for sudo");
		await SessionTeardown.ReleaseAsync(() => new ValueTask(CompanionConnection<SftpClient>.DisposeClientAsync(_client)), _logger, id, "the SFTP connection");
		SudoPasswords.Dispose();
		AccountNames.Dispose();
		await SessionTeardown.ReleaseAsync(Context.DisposeAsync, _logger, id, "the login and jump hosts");
		_logger.LogInformation("SFTP session {SessionId} closed", id);
	}

	private async Task WatchAsync()
	{
		Exception cause = await _monitor.Lost;
		if (!_completion.TrySetException(cause))
		{
			return;
		}

		if (cause is not KeepAliveTimeoutException)
		{
			_logger.LogInformation(cause, "SFTP session {SessionId} lost its connection", Context.SessionId);
			return;
		}

		// The socket still looks open, so nothing else would let go of it before TCP gives up.
		_logger.LogInformation("SFTP session {SessionId} stopped answering keepalives and is closed: {Cause}", Context.SessionId, LogSafe.Describe(cause));
		await DisposeAsync();
	}
}
