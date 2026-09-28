using System.Text;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Modules.Ssh.Connection;
using Mokaterm.Modules.Ssh.Elevation;
using Mokaterm.Modules.Ssh.FileSystem;
using Mokaterm.Modules.Ssh.Terminal;
using Mokaterm.Modules.Ssh.Tunnels;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace Mokaterm.Modules.Ssh.Sessions;

/// <summary>
/// A terminal session: the SSH connection, its shell, and files over a companion SFTP connection that reuses the login and
/// the trusted host key.
/// </summary>
internal sealed class SshSession : IProtocolSession, ISshSessionHost
{
	// SSH.NET closes the shell on a server disconnect before the socket goes away; this long the monitor gets to notice.
	private static readonly TimeSpan ShellEndGracePeriod = TimeSpan.FromSeconds(1);

	private readonly SshClient _client;
	private readonly SshTerminalChannel _terminal;
	private readonly CompanionConnection<SftpClient> _sftp;
	private readonly SshFileSystemFeature _fileSystem;
	private readonly SshTunnelManager _tunnels;
	private readonly ConnectionMonitor _monitor;
	private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly ILogger _logger;
	private int _disposed;

	private SshSession(SshConnector connector, SshConnection<SshClient> connection, SshTerminalChannel terminal, ILogger logger)
	{
		Connector = connector;
		Context = connection.Context;
		_client = connection.Client;
		_terminal = terminal;
		_logger = logger;
		AccountNames = new RemoteAccountNames(() => ConnectedSshClient, logger);
		SudoPasswords = new SudoPasswordCache();
		_sftp = new CompanionConnection<SftpClient>(cancellationToken => connector.ConnectCompanionAsync(Context, SftpClients.Create, cancellationToken));
		_fileSystem = new SshFileSystemFeature(this, logger);
		_tunnels = new SshTunnelManager(_client, Context.SessionId, Context.Interaction, logger);
		_monitor = new ConnectionMonitor(_client);
		_ = WatchAsync();
	}

	public Task Completion => _completion.Task;

	public SshSessionContext Context { get; }

	public SshConnector Connector { get; }

	public RemoteAccountNames AccountNames { get; }

	public SudoPasswordCache SudoPasswords { get; }

	public SshClient? ConnectedSshClient => ConnectionMonitor.IsConnected(_client) ? _client : null;

	/// <summary>
	/// Opens the shell on a connected client and returns the running session. Takes ownership of
	/// <paramref name="connection"/>, which is closed if the shell cannot start.
	/// </summary>
	/// <exception cref="ProtocolConnectException">The server refused the shell.</exception>
	public static async Task<SshSession> StartAsync(SshConnector connector, SshConnection<SshClient> connection, TerminalSize size, ILogger logger, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(connector);
		ArgumentNullException.ThrowIfNull(connection);
		SshConnectionOptions options = connection.Context.Options;
		string terminalType = SshConnectionOptions.IsValidTerminalType(options.TerminalType)
			? options.TerminalType
			: connector.SettingsService.Get<TerminalSettings>().TerminalType;

		SshTerminalChannel terminal;
		try
		{
			terminal = await SshTerminalChannel.OpenAsync(connection.Client, terminalType, size, cancellationToken);
		}
		catch (Exception ex)
		{
			await CompanionConnection<SshClient>.DisposeClientAsync(connection.Client);
			await connection.Context.DisposeAsync();
			if (ex is SshException)
			{
				string endpoint = HostEndpoint.Format(connection.Context.Host, connection.Context.Port);
				throw new ProtocolConnectException(ConnectFailure.ProtocolError, $"{endpoint} refused to open a shell: {ex.Message}", ex);
			}

			throw;
		}

		SshSession session = new(connector, connection, terminal, logger);
		try
		{
			// Tunnels that cannot open leave a reason in the tunnels view; they never cost the user the session.
			await session._tunnels.StartSavedAsync(options.Tunnels, cancellationToken);
			if (options.StartupCommand is { } startupCommand)
			{
				await terminal.WriteAsync(Encoding.UTF8.GetBytes(startupCommand + "\n"), cancellationToken);
			}
		}
		catch
		{
			await session.DisposeAsync();
			throw;
		}

		return session;
	}

	public TFeature? GetFeature<TFeature>()
		where TFeature : class
	{
		if (typeof(TFeature) == typeof(ITerminalChannel))
		{
			return (TFeature)(object)_terminal;
		}

		if (typeof(TFeature) == typeof(ISshTunnelFeature))
		{
			return (TFeature)(object)_tunnels;
		}

		return typeof(TFeature) == typeof(IFileSystemFeature) ? (TFeature)(object)_fileSystem : null;
	}

	public async Task<SftpClient> GetSftpClientAsync(CancellationToken cancellationToken)
	{
		try
		{
			return await _sftp.GetAsync(cancellationToken);
		}
		catch (ProtocolConnectException ex)
		{
			throw new RemoteFileSystemException(RemoteFileErrorKind.ConnectionLost, $"The SFTP connection could not be opened: {ex.Message}", null, ex);
		}
		catch (ObjectDisposedException ex)
		{
			throw new RemoteFileSystemException(RemoteFileErrorKind.ConnectionLost, "The session is closed.", null, ex);
		}
	}

	public Task<SshClient> GetSshClientAsync(CancellationToken cancellationToken) =>
		ConnectedSshClient is { } client
			? Task.FromResult(client)
			: Task.FromException<SshClient>(new RemoteFileSystemException(RemoteFileErrorKind.ConnectionLost, "The SSH connection is closed."));

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		_completion.TrySetResult();

		// Each part closes even when one before it fails: the connection, the jump hosts and the login must not outlive the session.
		Guid id = Context.SessionId;
		await SessionTeardown.ReleaseAsync(_monitor.DisposeAsync, _logger, id, "the connection monitor");
		await SessionTeardown.ReleaseAsync(_fileSystem.DisposeAsync, _logger, id, "its file systems");
		await SessionTeardown.ReleaseAsync(_terminal.DisposeAsync, _logger, id, "the shell");
		await SessionTeardown.ReleaseAsync(_tunnels.DisposeAsync, _logger, id, "the tunnels");
		await SessionTeardown.ReleaseAsync(_sftp.DisposeAsync, _logger, id, "the SFTP connection");
		await SessionTeardown.ReleaseAsync(() => new ValueTask(CompanionConnection<SshClient>.DisposeClientAsync(_client)), _logger, id, "the SSH connection");
		SudoPasswords.Dispose();
		AccountNames.Dispose();
		await SessionTeardown.ReleaseAsync(Context.DisposeAsync, _logger, id, "the login and jump hosts");
		_logger.LogInformation("SSH session {SessionId} closed", id);
	}

	private async Task WatchAsync()
	{
		try
		{
			Task<Exception> lost = _monitor.Lost;
			if (await Task.WhenAny(_terminal.Ended, lost) == lost)
			{
				Fail(await lost);
				await _terminal.DisposeAsync();
				return;
			}

			Task decided = await Task.WhenAny(_terminal.ClosedByServer, lost, Task.Delay(ShellEndGracePeriod));
			if (decided == lost)
			{
				Fail(await lost);
			}
			else if (decided == _terminal.ClosedByServer || ConnectionMonitor.IsConnected(_client))
			{
				_completion.TrySetResult();
			}
			else
			{
				Fail(new SshConnectionException("The server closed the connection."));
			}
		}
		catch (Exception ex)
		{
			Fail(ex);
		}
	}

	private void Fail(Exception cause)
	{
		if (_completion.TrySetException(cause))
		{
			_logger.LogInformation(cause, "SSH session {SessionId} lost its connection", Context.SessionId);
		}
	}
}
