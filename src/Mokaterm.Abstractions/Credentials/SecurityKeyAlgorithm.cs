namespace Mokaterm.Abstractions.Credentials;

/// <summary>The two signature algorithms OpenSSH accepts from a FIDO security key.</summary>
public enum SecurityKeyAlgorithm
{
	/// <summary><c>sk-ecdsa-sha2-nistp256@openssh.com</c>, which every FIDO key and Windows Hello can make.</summary>
	EcdsaP256,

	/// <summary><c>sk-ssh-ed25519@openssh.com</c>, which needs a FIDO2 key that implements EdDSA.</summary>
	Ed25519,
}
