using System.Text;

namespace Mokaterm.Abstractions.Credentials;

/// <summary>
/// Secret values coming from an editor. On update, a null field keeps the stored value and an empty
/// string clears it. Encrypt and drop the instance quickly: strings cannot be wiped.
/// </summary>
public sealed record CredentialSecretInput
{
	public string? Password { get; init; }

	/// <summary>Private key text in OpenSSH or PEM format.</summary>
	public string? PrivateKey { get; init; }

	public string? Passphrase { get; init; }

	/// <summary>The password of the proxy the login dials through, when the proxy asks for one.</summary>
	public string? ProxyPassword { get; init; }

	private bool PrintMembers(StringBuilder builder)
	{
		builder
			.Append("Password = ").Append(SecretText.Describe(Password))
			.Append(", PrivateKey = ").Append(SecretText.Describe(PrivateKey))
			.Append(", Passphrase = ").Append(SecretText.Describe(Passphrase))
			.Append(", ProxyPassword = ").Append(SecretText.Describe(ProxyPassword));
		return true;
	}
}
