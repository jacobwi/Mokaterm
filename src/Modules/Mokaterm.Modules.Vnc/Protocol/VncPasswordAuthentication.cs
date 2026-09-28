using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace Mokaterm.Modules.Vnc.Protocol;

/// <summary>
/// The DES challenge of classic VNC authentication. The server sends sixteen random bytes and the client returns
/// them encrypted with the password as the key.
/// </summary>
[SuppressMessage(
	"Security",
	"CA5351:Do Not Use Broken Cryptographic Algorithms",
	Justification = "RFB security type 2 is defined as single DES with this exact key derivation; no other algorithm can answer the challenge.")]
internal static class VncPasswordAuthentication
{
	public const int ChallengeLength = 16;

	/// <summary>Only the first eight bytes of a password reach the key. Longer passwords are silently cut, as in every VNC server.</summary>
	public const int KeyLength = 8;

	/// <summary>Encrypts <paramref name="challenge"/> with <paramref name="password"/> as the DES key.</summary>
	/// <exception cref="CryptographicException">The password derives a key DES refuses, such as an empty password.</exception>
	public static byte[] CreateResponse(ReadOnlySpan<byte> challenge, ReadOnlySpan<byte> password)
	{
		if (challenge.Length != ChallengeLength)
		{
			throw new ArgumentException($"A VNC authentication challenge is {ChallengeLength} bytes.", nameof(challenge));
		}

		byte[] key = new byte[KeyLength];
		try
		{
			DeriveKey(password, key);
			using DES des = DES.Create();
			des.Key = key;

			// ECB over both eight byte halves is what the protocol asks for; there is no chaining and no padding.
			return des.EncryptEcb(challenge, PaddingMode.None);
		}
		finally
		{
			CryptographicOperations.ZeroMemory(key);
		}
	}

	/// <summary>
	/// Copies the password into an eight byte key, zero padded, with the bits of every byte reversed. The reversal
	/// comes from the original AT&amp;T implementation and every server repeats it.
	/// </summary>
	public static void DeriveKey(ReadOnlySpan<byte> password, Span<byte> key)
	{
		key.Clear();
		int length = Math.Min(password.Length, KeyLength);
		for (int i = 0; i < length; i++)
		{
			key[i] = ReverseBits(password[i]);
		}
	}

	private static byte ReverseBits(byte value)
	{
		uint bits = value;
		bits = ((bits & 0xF0) >> 4) | ((bits & 0x0F) << 4);
		bits = ((bits & 0xCC) >> 2) | ((bits & 0x33) << 2);
		bits = ((bits & 0xAA) >> 1) | ((bits & 0x55) << 1);
		return (byte)bits;
	}
}
