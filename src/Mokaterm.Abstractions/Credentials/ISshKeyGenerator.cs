namespace Mokaterm.Abstractions.Credentials;

/// <summary>Creates SSH key pairs in OpenSSH format and reads the public half back out of one.</summary>
public interface ISshKeyGenerator
{
	/// <summary>
	/// Generates a key pair. RSA and the passphrase KDF take seconds, so this runs off the caller's thread.
	/// </summary>
	Task<GeneratedSshKey> GenerateAsync(SshKeyRequest request, CancellationToken cancellationToken = default);

	/// <summary>
	/// Reads the public key out of an OpenSSH private key. The public half of the file is not encrypted, so this
	/// works without the passphrase, and the comment then stays empty. Null when the text is not an OpenSSH key.
	/// </summary>
	SshPublicKey? ReadPublicKey(string privateKey);
}
