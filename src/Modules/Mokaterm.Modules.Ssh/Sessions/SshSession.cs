using System.Text;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Modules.Ssh.Admin;
using Mokaterm.Modules.Ssh.Connection;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.Modules.Ssh.Elevation;
using Mokaterm.Modules.Ssh.FileSystem;
using Mokaterm.Modules.Ssh.Forwarding;
using Mokaterm.Modules.Ssh.Shell;
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
	private readonly SessionRootFiles _rootFiles;
	private readonly SshTunnelManager _tunnels;
	private readonly SshCommandFeature _commands;
	private readonly SshForwarding? _forwarding;
	private readonly SshForwardingIndicators? _indicators;
	private readonly SystemAdminFeature _admin;
	private readonly DockerSessionFeature _docker;
	private readonly ConnectionMonitor _monitor;
	private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly ILogger _logger;
	private int _disposed;

	private SshSession(SshConnector connector, SshConnection<SshClient> connection, SshTerminalChannel terminal, SshForwarding? forwarding, ILogger logger)
	{
		Connector = connector;
		Context = connection.Context;
		_client = connection.Client;
		_terminal = terminal;
		_forwarding = forwarding;
		_indicators = forwarding is null ? null : new SshForwardingIndicators(forwarding);
		_logger = logger;
		AccountNames = new RemoteAccountNames(() => ConnectedSshClient, logger);
		SudoPasswords = new SudoPasswordCache();
		_sftp = new CompanionConnection<SftpClient>(cancellationToken => connector.ConnectCompanionAsync(Context, SftpClients.Create, cancellationToken));
		_fileSystem = new SshFileSystemFeature(this, logger);
		_rootFiles = new SessionRootFiles(_fileSystem);
		_tunnels = new SshTunnelManager(_client, Context.SessionId, Context.Interaction, logger);
		_commands = new SshCommandFeature(GetSshClientAsync, connector.Time);
		_admin = new SystemAdminFeature(new SshAdminShell(this, logger), connector.Time, logger, _rootFiles);
		_docker = new DockerSessionFeature(
			new UserDockerShell(_commands),
			Context.Options.Elevation == SshElevationMode.None ? null : AuthenticateSudoAsync,
			connector.Time,
			Context.Host,
			Context.Credentials.Username,
			_fileSystem,
			() => connector.Settings.EffectiveStagingDirectory,
			logger,
			_rootFiles);
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
	/// <param name="hostKind">On the web host nothing on the machine running the connection is forwarded, since that machine is the server.</param>
	/// <exception cref="ProtocolConnectException">The server refused the shell.</exception>
	public static async Task<SshSession> StartAsync(SshConnector connector, SshConnection<SshClient> connection, TerminalSize size, HostKind hostKind, ILogger logger, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(connector);
		ArgumentNullException.ThrowIfNull(connection);
		SshConnectionOptions options = connection.Context.Options;
		string terminalType = SshConnectionOptions.IsValidTerminalType(options.TerminalType)
			? options.TerminalType
			: connector.SettingsService.Get<TerminalSettings>().TerminalType;

		SshForwarding? forwarding = null;
		SshTerminalChannel terminal;
		bool opened = false;
		try
		{
			// Listening starts before the shell exists: a login script may reach for the agent as soon as the shell runs.
			forwarding = SshForwarding.Start(connection.Client, CreateForwardingSetup(connector, connection.Context, hostKind), logger);
			forwarding?.BeginShellOpen();
			terminal = await SshTerminalChannel.OpenAsync(connection.Client, terminalType, size, cancellationToken);
			opened = true;
		}
		catch (Exception ex)
		{
			if (forwarding is not null)
			{
				await forwarding.DisposeAsync();
			}

			await CompanionConnection<SshClient>.DisposeClientAsync(connection.Client);
			await connection.Context.DisposeAsync();
			if (ex is SshException)
			{
				string endpoint = HostEndpoint.Format(connection.Context.Host, connection.Context.Port);
				throw new ProtocolConnectException(ConnectFailure.ProtocolError, $"{endpoint} refused to open a shell: {ex.Message}", ex);
			}

			throw;
		}
		finally
		{
			forwarding?.EndShellOpen(opened);
		}

		SshSession session = new(connector, connection, terminal, forwarding, logger);
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

		if (typeof(TFeature) == typeof(IRemoteCommandFeature))
		{
			return (TFeature)(object)_commands;
		}

		if (typeof(TFeature) == typeof(ISshForwardingFeature))
		{
			return (TFeature?)(object?)_forwarding;
		}

		if (typeof(TFeature) == typeof(ISessionIndicators))
		{
			return (TFeature?)(object?)_indicators;
		}

		if (typeof(TFeature) == typeof(SystemAdminFeature))
		{
			return (TFeature)(object)_admin;
		}

		if (typeof(TFeature) == typeof(DockerSessionFeature))
		{
			return (TFeature)(object)_docker;
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
		await SessionTeardown.ReleaseAsync(_docker.DisposeAsync, _logger, id, "the Docker manager");
		await SessionTeardown.ReleaseAsync(_admin.DisposeAsync, _logger, id, "the system view");
		await SessionTeardown.ReleaseAsync(_rootFiles.DisposeAsync, _logger, id, "the tools' root file system");
		await SessionTeardown.ReleaseAsync(_fileSystem.DisposeAsync, _logger, id, "its file systems");
		await SessionTeardown.ReleaseAsync(_terminal.DisposeAsync, _logger, id, "the shell");
		await SessionTeardown.ReleaseAsync(_tunnels.DisposeAsync, _logger, id, "the tunnels");
		if (_forwarding is { } forwarding)
		{
			await SessionTeardown.ReleaseAsync(forwarding.DisposeAsync, _logger, id, "agent and X11 forwarding");
		}

		await SessionTeardown.ReleaseAsync(_sftp.DisposeAsync, _logger, id, "the SFTP connection");
		await SessionTeardown.ReleaseAsync(() => new ValueTask(CompanionConnection<SshClient>.DisposeClientAsync(_client)), _logger, id, "the SSH connection");
		SudoPasswords.Dispose();
		AccountNames.Dispose();
		await SessionTeardown.ReleaseAsync(Context.DisposeAsync, _logger, id, "the login and jump hosts");
		_logger.LogInformation("SSH session {SessionId} closed", id);
	}

	private static SshForwardingSetup CreateForwardingSetup(SshConnector connector, SshSessionContext context, HostKind hostKind)
	{
		SshSettings settings = connector.Settings;
		return new SshForwardingSetup
		{
			SessionId = context.SessionId,
			Options = context.Options.Forwarding,
			Account = context.Account,
			OffersSystemAgent = hostKind == HostKind.Desktop,
			OffersX11 = hostKind == HostKind.Desktop,
			AgentEndpoint = settings.AgentEndpoint,
			Credentials = context.Credentials,
			Interaction = context.Interaction,
		};
	}

	/// <summary>Gets sudo going for the Docker manager, asking for the password the way the file browser does.</summary>
	private async Task<SudoRunner> AuthenticateSudoAsync(CancellationToken cancellationToken) =>
		await SudoRunner.AuthenticateAsync(SudoLogin.ForSession(this, await GetSshClientAsync(cancellationToken)), cancellationToken);

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
