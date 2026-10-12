using System.Text;

namespace Mokaterm.Modules.Ssh.Admin.Accounts;

/// <summary>One key line of an <c>authorized_keys</c> file, as sshd reads it: options, type, key and comment.</summary>
internal sealed record AuthorizedKey
{
	/// <summary>The line's number in the file, from 1.</summary>
	public required int LineNumber { get; init; }

	/// <summary>What comes before the key, such as <c>from="10.0.0.0/8",no-pty</c>; empty when there is none.</summary>
	public string Options { get; init; } = "";

	/// <summary>The key type as the line names it, such as <c>ssh-ed25519</c>.</summary>
	public required string Algorithm { get; init; }

	/// <summary>The key itself, base64, exactly as the line holds it. This is what makes two lines the same key.</summary>
	public required string Blob { get; init; }

	public string Comment { get; init; } = "";

	/// <summary>The <c>SHA256:...</c> fingerprint, as <c>ssh-keygen -l</c> prints it and the keychain stores it.</summary>
	public required string Fingerprint { get; init; }

	/// <summary>An OpenSSH certificate rather than a plain key.</summary>
	public bool IsCertificate => Algorithm.EndsWith("-cert-v01@openssh.com", StringComparison.Ordinal);

	// Options can carry a forced command or an environment value; the fingerprint names the key well enough.
	private bool PrintMembers(StringBuilder builder)
	{
		builder.Append("LineNumber = ").Append(LineNumber).Append(", Algorithm = ").Append(Algorithm).Append(", Fingerprint = ").Append(Fingerprint);
		return true;
	}
}
