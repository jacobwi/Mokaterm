using Mokaterm.Core.Security;

namespace Mokaterm.Core.Storage;

/// <summary>The on-disk layout of an encrypted document: <c>"MKV1" | nonce (12) | tag (16) | ciphertext</c>.</summary>
internal static class VaultEnvelope
{
	public const int MagicSize = 4;

	public static ReadOnlySpan<byte> Magic => "MKV1"u8;

	public static byte[] Seal(IVaultCipher cipher, ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData)
	{
		byte[] envelope = new byte[MagicSize + AesGcmBox.GetSealedLength(plaintext.Length)];
		Magic.CopyTo(envelope);
		cipher.Encrypt(plaintext, associatedData, envelope.AsSpan(MagicSize));
		return envelope;
	}

	/// <summary>Decrypts into pinned memory that the caller disposes.</summary>
	/// <exception cref="InvalidDataException">The bytes are not a vault document.</exception>
	/// <exception cref="System.Security.Cryptography.AuthenticationTagMismatchException">The document was tampered with or belongs elsewhere.</exception>
	public static PinnedBytes Open(IVaultCipher cipher, ReadOnlySpan<byte> envelope, ReadOnlySpan<byte> associatedData)
	{
		if (envelope.Length < MagicSize + AesGcmBox.Overhead || !envelope.StartsWith(Magic))
		{
			throw new InvalidDataException("The file is not a Mokaterm vault document.");
		}

		return cipher.DecryptToPinned(envelope[MagicSize..], associatedData);
	}
}
