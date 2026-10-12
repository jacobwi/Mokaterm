namespace Mokaterm.Abstractions.Credentials;

/// <summary>
/// The public half of a key saved in the vault, the way the keychain's "Copy public key" and the system view's
/// authorized keys both read it: the secret is decrypted, the public half is read out of the OpenSSH key file and the
/// secret is wiped again before this returns. The public half of the file is not encrypted, so a key's passphrase is
/// never needed.
/// </summary>
public static class CredentialPublicKey
{
	/// <summary>
	/// Reads the public key of credential <paramref name="credentialId"/>. <c>HasKey</c> is false when the credential
	/// holds no private key at all; <c>Key</c> is null when it holds one that is not in OpenSSH format.
	/// </summary>
	/// <exception cref="Security.VaultLockedException">The vault is locked.</exception>
	/// <exception cref="KeyNotFoundException">No credential with that id.</exception>
	public static async Task<(bool HasKey, SshPublicKey? Key)> ReadAsync(
		ICredentialStore store,
		ISshKeyGenerator keys,
		Guid credentialId,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(keys);
		using CredentialSecret secret = await store.RevealAsync(credentialId, cancellationToken);
		return secret.PrivateKey is { } privateKey ? (true, keys.ReadPublicKey(privateKey.RevealString())) : (false, null);
	}
}
