using Mokaterm.Modules.Ssh.Connection;
using Mokaterm.Modules.Ssh.Elevation;
using Mokaterm.Modules.Ssh.FileSystem;
using Renci.SshNet;

namespace Mokaterm.Modules.Ssh.Sessions;

/// <summary>What the file system feature needs from the ssh or sftp session that owns it.</summary>
internal interface ISshSessionHost
{
	SshSessionContext Context { get; }

	SshConnector Connector { get; }

	RemoteAccountNames AccountNames { get; }

	SudoPasswordCache SudoPasswords { get; }

	/// <summary>The session's SSH client when it has one and it is connected, without opening anything.</summary>
	SshClient? ConnectedSshClient { get; }

	/// <summary>The SFTP client, opening the companion connection when the session has none yet.</summary>
	/// <exception cref="Mokaterm.Abstractions.FileSystem.RemoteFileSystemException">The connection is gone or could not be opened.</exception>
	Task<SftpClient> GetSftpClientAsync(CancellationToken cancellationToken);

	/// <summary>An SSH client for exec channels, opening the companion connection when the session has none yet.</summary>
	/// <exception cref="Mokaterm.Abstractions.FileSystem.RemoteFileSystemException">The connection is gone or could not be opened.</exception>
	Task<SshClient> GetSshClientAsync(CancellationToken cancellationToken);
}
