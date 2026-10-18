using System.Security.Cryptography;
using Mokaterm.Core.Security;

namespace Mokaterm.Core.Keys;

/// <summary>
/// AES in CBC mode without padding, decrypting in place. OpenSSH key files may use it and PuTTY's always do, and both pad
/// the plaintext themselves, so the length is always whole blocks.
/// </summary>
internal static class AesCbc
{
	public const int BlockSize = 16;

	public static void Decrypt(ReadOnlySpan<byte> key, ReadOnlySpan<byte> iv, Span<byte> data)
	{
		if (data.Length % BlockSize != 0)
		{
			throw new ArgumentException("CBC data must be whole blocks.", nameof(data));
		}

		using Aes aes = Aes.Create();

		// The setter keeps its own copy, which Clear wipes; this one would otherwise stay on the heap.
		byte[] keyCopy = key.ToArray();
		using PinnedBytes plain = new(data.Length);
		try
		{
			aes.Key = keyCopy;
			aes.DecryptCbc(data, iv, plain.Span, PaddingMode.None);
			plain.Span.CopyTo(data);
		}
		finally
		{
			CryptographicOperations.ZeroMemory(keyCopy);
			aes.Clear();
		}
	}
}
