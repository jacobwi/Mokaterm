namespace Mokaterm.Core.Security;

internal static class VaultCipherExtensions
{
	public static byte[] EncryptToArray(this IVaultCipher cipher, ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData)
	{
		byte[] sealedBox = new byte[AesGcmBox.GetSealedLength(plaintext.Length)];
		cipher.Encrypt(plaintext, associatedData, sealedBox);
		return sealedBox;
	}

	/// <summary>Decrypts into pinned memory that the caller disposes.</summary>
	public static PinnedBytes DecryptToPinned(this IVaultCipher cipher, ReadOnlySpan<byte> sealedBox, ReadOnlySpan<byte> associatedData)
	{
		PinnedBytes plaintext = new(AesGcmBox.GetPlaintextLength(sealedBox.Length));
		try
		{
			cipher.Decrypt(sealedBox, associatedData, plaintext.Span);
			return plaintext;
		}
		catch
		{
			plaintext.Dispose();
			throw;
		}
	}
}
