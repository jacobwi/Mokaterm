using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace Mokaterm.Core.Keys;

/// <summary>
/// AES in counter mode, which .NET does not ship. OpenSSH private key files use <c>aes256-ctr</c>, so writing one
/// means building the keystream from AES over the counter block.
/// </summary>
internal static class AesCtr
{
	private const int BlockSize = 16;

	/// <summary>Encrypts or decrypts <paramref name="data"/> in place; counter mode makes both the same operation.</summary>
	[SuppressMessage("Security", "CA5358:Review cipher mode usage with cryptography experts", Justification = "ECB builds the counter keystream; the data itself is never encrypted with ECB.")]
	public static void Transform(ReadOnlySpan<byte> key, ReadOnlySpan<byte> iv, Span<byte> data)
	{
		if (iv.Length != BlockSize)
		{
			throw new ArgumentException("The counter block must be 16 bytes.", nameof(iv));
		}

		using Aes aes = Aes.Create();
		aes.Mode = CipherMode.ECB;
		aes.Padding = PaddingMode.None;

		// The setter keeps its own copy, which Clear wipes; this one would otherwise stay on the heap.
		byte[] keyCopy = key.ToArray();
		try
		{
			aes.Key = keyCopy;
		}
		finally
		{
			CryptographicOperations.ZeroMemory(keyCopy);
		}

		Span<byte> counter = stackalloc byte[BlockSize];
		Span<byte> keystream = stackalloc byte[BlockSize];
		try
		{
			iv.CopyTo(counter);
			for (int offset = 0; offset < data.Length; offset += BlockSize)
			{
				aes.EncryptEcb(counter, keystream, PaddingMode.None);
				int length = Math.Min(BlockSize, data.Length - offset);
				for (int i = 0; i < length; i++)
				{
					data[offset + i] ^= keystream[i];
				}

				Increment(counter);
			}
		}
		finally
		{
			CryptographicOperations.ZeroMemory(keystream);
			CryptographicOperations.ZeroMemory(counter);
			aes.Clear();
		}
	}

	private static void Increment(Span<byte> counter)
	{
		for (int i = counter.Length - 1; i >= 0; i--)
		{
			if (++counter[i] != 0)
			{
				return;
			}
		}
	}
}
