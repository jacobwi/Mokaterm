using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Ssh.Connection;
using Mokaterm.Modules.Ssh.Sessions;
using Renci.SshNet;
using LoginMethod = Mokaterm.Abstractions.Connections.AuthenticationMethod;

namespace Mokaterm.Modules.Ssh.Protocols;

/// <summary>File-only sessions over SFTP, a variant of SSH.</summary>
internal sealed class SftpProtocolProvider : IProtocolProvider
{
	private readonly SshConnector _connector;
	private readonly ILogger<SftpSession> _logger;

	public SftpProtocolProvider(SshConnector connector, ILoggerFactory? loggerFactory = null)
	{
		_connector = connector;
		_logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<SftpSession>();
	}

	public static ProtocolDescriptor SftpDescriptor { get; } = new()
	{
		Id = SshProtocolIds.Sftp,
		DisplayName = "SFTP",
		Description = "Files over SSH without a terminal.",
		DefaultPort = 22,
		Capabilities = ProtocolCapabilities.FileSystem | ProtocolCapabilities.Elevation,
		AuthenticationMethods = [LoginMethod.Password, LoginMethod.PublicKey, LoginMethod.KeyboardInteractive, LoginMethod.Agent],
		VariantOf = SshProtocolIds.Ssh,
		Order = 1,
	};

	public ProtocolDescriptor Descriptor => SftpDescriptor;

	public async Task<IProtocolSession> ConnectAsync(ProtocolConnectContext context, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(context);
		SshConnection<SftpClient> connection = await _connector.ConnectAsync(context, SftpClients.Create, cancellationToken);
		return new SftpSession(_connector, connection, _logger);
	}
}
