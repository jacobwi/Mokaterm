using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Ssh.Connection;
using Mokaterm.Modules.Ssh.Forwarding;
using Mokaterm.Modules.Ssh.Sessions;
using Renci.SshNet;
using LoginMethod = Mokaterm.Abstractions.Connections.AuthenticationMethod;

namespace Mokaterm.Modules.Ssh.Protocols;

/// <summary>SSH terminal sessions with an SFTP file browser beside them, and command-only connections for command runs and the fleet overview.</summary>
internal sealed class SshProtocolProvider : IProtocolProvider
{
	private readonly SshConnector _connector;
	private readonly ILogger<SshSession> _logger;
	private readonly ILogger<SshCommandSession> _commandLogger;
	private readonly HostKind _hostKind;

	/// <param name="environment">The host; without one this is taken to be the desktop app.</param>
	public SshProtocolProvider(SshConnector connector, ILoggerFactory? loggerFactory = null, IAppEnvironment? environment = null)
	{
		_connector = connector;
		_hostKind = environment?.Kind ?? HostKind.Desktop;
		ILoggerFactory factory = loggerFactory ?? NullLoggerFactory.Instance;
		_logger = factory.CreateLogger<SshSession>();
		_commandLogger = factory.CreateLogger<SshCommandSession>();
	}

	public static ProtocolDescriptor SshDescriptor { get; } = new()
	{
		Id = SshProtocolIds.Ssh,
		DisplayName = "SSH",
		Description = "Secure shell with a file browser over SFTP.",
		QuickConnectExample = "user@host:port",
		DefaultPort = 22,
		Capabilities = ProtocolCapabilities.Terminal | ProtocolCapabilities.FileSystem | ProtocolCapabilities.Elevation | ProtocolCapabilities.Commands | ProtocolCapabilities.HostSummary,
		AuthenticationMethods = [LoginMethod.Password, LoginMethod.PublicKey, LoginMethod.KeyboardInteractive, LoginMethod.Agent],
		Order = 0,
	};

	public ProtocolDescriptor Descriptor => SshDescriptor;

	public async Task<IProtocolSession> ConnectAsync(ProtocolConnectContext context, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(context);
		if (context.Purpose != ConnectPurpose.Commands && SshConnectionOptions.FromOptions(context.Connection.Options).Forwarding.ForwardAgent)
		{
			// Before connecting: a connection takes SSH.NET's message table as it stands when it logs in, and a server
			// opening an agent channel on a connection that cannot read one would end it.
			_ = SshNetInternals.AcceptUnknownChannelTypes();
		}

		SshConnection<SshClient> connection = await _connector.ConnectAsync(context, static info => new SshClient(info), cancellationToken);
		if (context.Purpose == ConnectPurpose.Commands)
		{
			// The login and nothing else: a run across many machines must not open shells, bind tunnel ports or type a
			// startup command on any of them.
			return new SshCommandSession(connection, _connector.Time, _commandLogger);
		}

		context.Status?.Report("Opening shell");
		return await SshSession.StartAsync(_connector, connection, context.TerminalSize, _hostKind, _logger, cancellationToken);
	}
}
