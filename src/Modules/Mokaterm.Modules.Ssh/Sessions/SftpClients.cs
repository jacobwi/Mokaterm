using Renci.SshNet;

namespace Mokaterm.Modules.Ssh.Sessions;

/// <summary>How every SFTP client in the module is configured.</summary>
internal static class SftpClients
{
	// SSH.NET caps each request at the server's packet size, so this is an upper bound rather than a promise.
	private const uint BufferSize = 128 * 1024;

	// Without a limit one stuck request would hold the file system's lock forever.
	private static readonly TimeSpan OperationTimeout = TimeSpan.FromMinutes(2);

	public static SftpClient Create(ConnectionInfo connectionInfo) => new(connectionInfo)
	{
		BufferSize = BufferSize,
		OperationTimeout = OperationTimeout,
	};
}
