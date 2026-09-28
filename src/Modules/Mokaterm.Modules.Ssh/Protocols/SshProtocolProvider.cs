using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Ssh.Connection;
using Mokaterm.Modules.Ssh.Sessions;
using Renci.SshNet;
using LoginMethod = Mokaterm.Abstractions.Connections.AuthenticationMethod;

namespace Mokaterm.Modules.Ssh.Protocols;

/// <summary>SSH terminal sessions with an SFTP file browser beside them.</summary>
internal sealed class SshProtocolProvider : IProtocolProvider
{
	private readonly SshConnector _connector;
	private readonly ILogger<SshSession> _logger;

	public SshProtocolProvider(SshConnector connector, ILoggerFactory? loggerFactory = null)
	{
		_connector = connector;
		_logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<SshSession>();
	}

	public static ProtocolDescriptor SshDescriptor { get; } = new()
	{
		Id = SshProtocolIds.Ssh,
		DisplayName = "SSH",
		Description = "Secure shell with a file browser over SFTP.",
		DefaultPort = 22,
		Capabilities = ProtocolCapabilities.Terminal | ProtocolCapabilities.FileSystem | ProtocolCapabilities.Elevation,
		AuthenticationMethods = [LoginMethod.Password, LoginMethod.PublicKey, LoginMethod.KeyboardInteractive, LoginMethod.Agent],
		Order = 0,
	};

	public ProtocolDescriptor Descriptor => SshDescriptor;

	public async Task<IProtocolSession> ConnectAsync(ProtocolConnectContext context, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(context);
		SshConnection<SshClient> connection = await _connector.ConnectAsync(context, static info => new SshClient(info), cancellationToken);
		context.Status?.Report("Opening shell");
		return await SshSession.StartAsync(_connector, connection, context.TerminalSize, _logger, cancellationToken);
	}
}
