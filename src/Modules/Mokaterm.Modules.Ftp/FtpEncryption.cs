namespace Mokaterm.Modules.Ftp;

/// <summary>How an FTP connection is secured.</summary>
public enum FtpEncryption
{
	/// <summary>Plain FTP. The password and file contents travel unencrypted.</summary>
	None,

	/// <summary>Starts in plain text and upgrades with <c>AUTH TLS</c>, usually on port 21.</summary>
	Explicit,

	/// <summary>TLS from the first byte, usually on port 990.</summary>
	Implicit,
}
