namespace Mokaterm.Modules.Ssh;

/// <summary>Protocol ids the SSH module registers.</summary>
public static class SshProtocolIds
{
	public const string Ssh = "ssh";

	/// <summary>File-only sessions over SFTP, a variant of <see cref="Ssh"/>.</summary>
	public const string Sftp = "sftp";
}
