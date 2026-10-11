using System.Text;

namespace Mokaterm.Abstractions.Credentials;

/// <summary>What to generate. Drop the instance as soon as the key is made: strings cannot be wiped.</summary>
public sealed record SshKeyRequest
{
	public required SshKeyType Type { get; init; }

	/// <summary>Trailing label on the <c>authorized_keys</c> line, usually <c>user@machine</c>.</summary>
	public string? Comment { get; init; }

	/// <summary>Encrypts the private key file. Null or empty writes it unprotected.</summary>
	public string? Passphrase { get; init; }

	/// <summary>
	/// The application string a security key credential is bound to, <c>ssh:</c> when null. Ignored for other types.
	/// </summary>
	public string? Application { get; init; }

	/// <summary>A security key that asks for its PIN or a fingerprint on every login (<c>verify-required</c>).</summary>
	public bool RequireUserVerification { get; init; }

	/// <summary>A security key credential kept on the device, so <c>ssh-keygen -K</c> can load it again.</summary>
	public bool ResidentKey { get; init; }

	public bool IsSecurityKey => Type is SshKeyType.EcdsaSecurityKey or SshKeyType.Ed25519SecurityKey;

	private bool PrintMembers(StringBuilder builder)
	{
		builder
			.Append("Type = ").Append(Type)
			.Append(", Comment = ").Append(Comment)
			.Append(", Passphrase = ").Append(SecretText.Describe(Passphrase))
			.Append(", Application = ").Append(Application)
			.Append(", RequireUserVerification = ").Append(RequireUserVerification)
			.Append(", ResidentKey = ").Append(ResidentKey);
		return true;
	}
}
