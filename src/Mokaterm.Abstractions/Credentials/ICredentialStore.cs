namespace Mokaterm.Abstractions.Credentials;

/// <summary>
/// Passwords and private keys, each secret encrypted individually inside the encrypted vault. Every
/// member throws <see cref="Security.VaultLockedException"/> while the vault is locked.
/// </summary>
public interface ICredentialStore
{
	event Action? Changed;

	ValueTask<IReadOnlyList<CredentialInfo>> ListAsync(CancellationToken cancellationToken = default);

	ValueTask<CredentialInfo?> FindAsync(Guid id, CancellationToken cancellationToken = default);

	/// <summary>
	/// Inserts or updates a credential. For a new credential <paramref name="secret"/> is required. Private
	/// keys are inspected with <see cref="IPrivateKeyInspector"/> when one is registered, which fills in the
	/// algorithm and fingerprint and rejects keys that do not parse.
	/// </summary>
	/// <exception cref="CredentialValidationException">The key could not be read or the passphrase is wrong.</exception>
	ValueTask<CredentialInfo> SaveAsync(CredentialInfo info, CredentialSecretInput? secret, CancellationToken cancellationToken = default);

	/// <summary>Decrypts the secret. The caller owns and must dispose the result.</summary>
	/// <exception cref="KeyNotFoundException">No credential with that id.</exception>
	ValueTask<CredentialSecret> RevealAsync(Guid id, CancellationToken cancellationToken = default);

	Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
