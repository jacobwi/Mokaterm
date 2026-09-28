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
}
