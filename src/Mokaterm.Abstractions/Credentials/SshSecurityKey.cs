using System.Text;

namespace Mokaterm.Abstractions.Credentials;

/// <summary>
/// An OpenSSH security key file once read: everything a signature needs except the authenticator. The private half of
/// the key never leaves the device; <see cref="KeyHandle"/> is what the device needs to find or rebuild it, which is why
/// the file keeps it in its private section.
/// </summary>
public sealed record SshSecurityKey
{
	public required SecurityKeyAlgorithm Algorithm { get; init; }

	/// <summary>The public key blob, as the server sees it and <c>authorized_keys</c> holds it.</summary>
	public required byte[] PublicKeyBlob { get; init; }

	/// <summary>The relying party the credential was made for, usually <c>ssh:</c>.</summary>
	public required string Application { get; init; }

	public required SecurityKeyFlags Flags { get; init; }

	/// <summary>The FIDO credential id. Useless without the device, but kept out of logs with the rest of the file.</summary>
	public required byte[] KeyHandle { get; init; }

	public string Comment { get; init; } = "";

	public string KeyType => SecurityKeyTypes.KeyType(Algorithm);

	public bool RequiresUserVerification => Flags.HasFlag(SecurityKeyFlags.UserVerificationRequired);

	private bool PrintMembers(StringBuilder builder)
	{
		builder
			.Append("Algorithm = ").Append(Algorithm)
			.Append(", Application = ").Append(Application)
			.Append(", Flags = ").Append(Flags)
			.Append(", KeyHandle = ").Append(KeyHandle.Length == 0 ? "(empty)" : "(set)")
			.Append(", Comment = ").Append(Comment);
		return true;
	}
}
