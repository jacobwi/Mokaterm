namespace Mokaterm.Abstractions.Credentials;

/// <summary>Key types Mokaterm can create.</summary>
public enum SshKeyType
{
	/// <summary>Small, fast and the usual choice.</summary>
	Ed25519,

	/// <summary>RSA 3072 bit, for servers that still refuse anything else.</summary>
	Rsa3072,

	/// <summary>RSA 4096 bit.</summary>
	Rsa4096,

	/// <summary>
	/// <c>sk-ecdsa-sha2-nistp256@openssh.com</c>: the private key stays on a FIDO security key or in Windows Hello, and the
	/// file only names it. Needs a host that registers an <see cref="Security.ISecurityKeyAuthenticator"/>.
	/// </summary>
	EcdsaSecurityKey,

	/// <summary><c>sk-ssh-ed25519@openssh.com</c>, the same on a FIDO2 key that implements EdDSA.</summary>
	Ed25519SecurityKey,
}
