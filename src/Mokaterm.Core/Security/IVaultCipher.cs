using Mokaterm.Abstractions.Security;

namespace Mokaterm.Core.Security;

/// <summary>
/// Encryption with the vault's data key for the stores in this assembly. Resolves to the scope's
/// <see cref="Vault"/>. Every member throws <see cref="VaultLockedException"/> while the vault is locked.
/// </summary>
internal interface IVaultCipher
{
	/// <summary>Id of the unlocked vault, part of the associated data that binds documents to it.</summary>
	Guid VaultId { get; }

	/// <summary>Encrypts into <paramref name="destination"/>, which must be <see cref="AesGcmBox.GetSealedLength"/> bytes long.</summary>
	void Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData, Span<byte> destination);

	/// <summary>Decrypts into <paramref name="destination"/>, which must be <see cref="AesGcmBox.GetPlaintextLength"/> bytes long.</summary>
	/// <exception cref="System.Security.Cryptography.AuthenticationTagMismatchException">The data was tampered with or belongs to another vault.</exception>
	void Decrypt(ReadOnlySpan<byte> sealedBox, ReadOnlySpan<byte> associatedData, Span<byte> destination);
}
