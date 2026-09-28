using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Mokaterm.Core.Keys;

/// <summary>
/// OpenBSD's <c>bcrypt_pbkdf</c>. OpenSSH private key files name exactly one KDF, <c>bcrypt</c>, so a passphrase
/// protected key cannot be written without it.
/// </summary>
internal static class BcryptPbkdf
{
	/// <summary>Bytes one bcrypt hash produces.</summary>
	private const int HashSize = 32;

	private const int Sha512Size = 64;

	/// <summary>What OpenSSH uses when it is not told otherwise.</summary>
	public const int DefaultRounds = 16;

	private static ReadOnlySpan<byte> Magic => "OxychromaticBlowfishSwatDynamite"u8;

	/// <summary>Fills <paramref name="output"/> with key material derived from the passphrase.</summary>
	public static void DeriveKey(ReadOnlySpan<byte> password, ReadOnlySpan<byte> salt, int rounds, Span<byte> output)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(rounds, 1);
		if (salt.IsEmpty || output.IsEmpty)
		{
			throw new ArgumentException("bcrypt_pbkdf needs a salt and an output buffer.", nameof(salt));
		}

		int keyLength = output.Length;
		int stride = ((keyLength + HashSize) - 1) / HashSize;
		int amount = ((keyLength + stride) - 1) / stride;

		Span<byte> passwordHash = stackalloc byte[Sha512Size];
		Span<byte> saltHash = stackalloc byte[Sha512Size];
		Span<byte> round = stackalloc byte[HashSize];
		Span<byte> mixed = stackalloc byte[HashSize];
		byte[] countedSalt = new byte[salt.Length + 4];
		try
		{
			SHA512.HashData(password, passwordHash);
			salt.CopyTo(countedSalt);
			output.Clear();

			int remaining = keyLength;
			for (uint count = 1; remaining > 0; count++)
			{
				BinaryPrimitives.WriteUInt32BigEndian(countedSalt.AsSpan(salt.Length), count);
				SHA512.HashData(countedSalt, saltHash);
				Hash(passwordHash, saltHash, round);
				round.CopyTo(mixed);

				for (int iteration = 1; iteration < rounds; iteration++)
				{
					SHA512.HashData(round, saltHash);
					Hash(passwordHash, saltHash, round);
					for (int i = 0; i < HashSize; i++)
					{
						mixed[i] ^= round[i];
					}
				}

				// The bytes are spread across the output rather than appended, which is where bcrypt_pbkdf
				// deviates from PBKDF2.
				int take = Math.Min(amount, remaining);
				int written = 0;
				while (written < take)
				{
					int destination = (written * stride) + (int)(count - 1);
					if (destination >= keyLength)
					{
						break;
					}

					output[destination] = mixed[written];
					written++;
				}

				if (written == 0)
				{
					break;
				}

				remaining -= written;
			}
		}
		finally
		{
			CryptographicOperations.ZeroMemory(passwordHash);
			CryptographicOperations.ZeroMemory(saltHash);
			CryptographicOperations.ZeroMemory(round);
			CryptographicOperations.ZeroMemory(mixed);
			CryptographicOperations.ZeroMemory(countedSalt);
		}
	}

	/// <summary>One bcrypt hash: 64 Blowfish key schedules, then the magic string encrypted 64 times.</summary>
	private static void Hash(ReadOnlySpan<byte> passwordHash, ReadOnlySpan<byte> saltHash, Span<byte> output)
	{
		Blowfish state = new();
		state.Expand(passwordHash, saltHash);
		for (int i = 0; i < 64; i++)
		{
			state.Expand(saltHash, default);
			state.Expand(passwordHash, default);
		}

		Span<uint> words = stackalloc uint[HashSize / 4];
		for (int i = 0; i < words.Length; i++)
		{
			words[i] = BinaryPrimitives.ReadUInt32BigEndian(Magic[(i * 4)..]);
		}

		for (int i = 0; i < 64; i++)
		{
			for (int block = 0; block < words.Length; block += 2)
			{
				uint left = words[block];
				uint right = words[block + 1];
				state.Encipher(ref left, ref right);
				words[block] = left;
				words[block + 1] = right;
			}
		}

		for (int i = 0; i < words.Length; i++)
		{
			BinaryPrimitives.WriteUInt32LittleEndian(output[(i * 4)..], words[i]);
		}
	}
}
