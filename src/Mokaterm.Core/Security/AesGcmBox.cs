using System.Security.Cryptography;

namespace Mokaterm.Core.Security;

/// <summary>
/// AES-256-GCM with a random 96-bit nonce and a 128-bit tag. A sealed box is laid out as
/// <c>nonce | tag | ciphertext</c>.
/// </summary>
internal static class AesGcmBox
{
	public const int KeySize = 32;
	public const int NonceSize = 12;
	public const int TagSize = 16;
	public const int Overhead = NonceSize + TagSize;

	public static int GetSealedLength(int plaintextLength) => plaintextLength + Overhead;

	/// <exception cref="InvalidDataException">The box is too short to hold a nonce and a tag.</exception>
	public static int GetPlaintextLength(int sealedLength) =>
		sealedLength >= Overhead ? sealedLength - Overhead : throw new InvalidDataException("Encrypted data is truncated.");

	public static void Seal(ReadOnlySpan<byte> key, ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData, Span<byte> destination)
	{
		if (destination.Length != GetSealedLength(plaintext.Length))
		{
			throw new ArgumentException("The destination must be exactly the plaintext length plus the nonce and tag.", nameof(destination));
		}

		Span<byte> nonce = destination[..NonceSize];
		RandomNumberGenerator.Fill(nonce);
		Encrypt(key, nonce, plaintext, associatedData, destination.Slice(NonceSize, TagSize), destination[Overhead..]);
	}

	/// <exception cref="AuthenticationTagMismatchException">Wrong key, wrong associated data or tampered data.</exception>
	public static void Open(ReadOnlySpan<byte> key, ReadOnlySpan<byte> sealedBox, ReadOnlySpan<byte> associatedData, Span<byte> destination)
	{
		if (destination.Length != GetPlaintextLength(sealedBox.Length))
		{
			throw new ArgumentException("The destination must be exactly the sealed length minus the nonce and tag.", nameof(destination));
		}

		Decrypt(key, sealedBox[..NonceSize], sealedBox.Slice(NonceSize, TagSize), sealedBox[Overhead..], associatedData, destination);
	}

	public static void Encrypt(ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData, Span<byte> tag, Span<byte> ciphertext)
	{
		using AesGcm aes = new(key, TagSize);
		aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
	}

	/// <exception cref="AuthenticationTagMismatchException">Wrong key, wrong associated data or tampered data.</exception>
	public static void Decrypt(ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> tag, ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> associatedData, Span<byte> plaintext)
	{
		if (nonce.Length != NonceSize || tag.Length != TagSize)
		{
			throw new InvalidDataException("Encrypted data has a malformed nonce or tag.");
		}

		using AesGcm aes = new(key, TagSize);
		aes.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
	}
}
