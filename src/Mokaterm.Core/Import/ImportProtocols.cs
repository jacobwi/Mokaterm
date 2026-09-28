namespace Mokaterm.Core.Import;

/// <summary>
/// Protocol ids and option keys the import writes. The modules own these names; Core repeats them because an
/// imported login has to arrive configured and Core cannot reference a module. An id no module registered is
/// reported as skipped instead of being created.
/// </summary>
internal static class ImportProtocols
{
	public const string Ssh = "ssh";

	public const string Sftp = "sftp";

	public const string Ftp = "ftp";

	/// <summary>Planned, so PuTTY telnet sessions map to it and are skipped until a module registers it.</summary>
	public const string Telnet = "telnet";

	/// <summary>Key of <c>FtpConnectionOptions.Encryption</c> in a login's protocol options.</summary>
	public const string FtpEncryptionKey = "ftp.encryption";

	/// <summary><c>FtpEncryption.Explicit</c>: starts plain and upgrades with AUTH TLS.</summary>
	public const string FtpEncryptionExplicit = "Explicit";

	/// <summary><c>FtpEncryption.Implicit</c>: TLS from the first byte.</summary>
	public const string FtpEncryptionImplicit = "Implicit";
}
