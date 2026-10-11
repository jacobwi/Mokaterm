using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Mokaterm.Abstractions.Security;

/// <summary>
/// The fixed head of WebAuthn authenticator data: the SHA-256 of the relying party id, a flags byte and a big endian
/// signature counter. An OpenSSH security key signature carries the flags and the counter, and a server rebuilds the
/// hash from the key's application, so both the key file writer and the signer read this.
/// </summary>
public readonly record struct AuthenticatorDataHeader(byte Flags, uint Counter)
{
	/// <summary>32 bytes of hash, one of flags, four of counter.</summary>
	public const int Length = 37;

	public const byte UserPresent = 0x01;

	public const byte UserVerified = 0x04;

	public const byte AttestedCredentialData = 0x40;

	public const byte ExtensionData = 0x80;

	private const int FlagsOffset = 32;

	/// <summary>
	/// Reads the head of <paramref name="data"/>, and only when it was made for <paramref name="relyingPartyId"/>: a
	/// signature from another relying party would not verify on the server anyway, and this says so before it is sent.
	/// </summary>
	public static bool TryRead(ReadOnlySpan<byte> data, string relyingPartyId, out AuthenticatorDataHeader header)
	{
		ArgumentNullException.ThrowIfNull(relyingPartyId);
		header = default;
		if (data.Length < Length)
		{
			return false;
		}

		Span<byte> expected = stackalloc byte[SHA256.HashSizeInBytes];
		SHA256.HashData(Encoding.UTF8.GetBytes(relyingPartyId), expected);
		if (!data[..FlagsOffset].SequenceEqual(expected))
		{
			return false;
		}

		header = new AuthenticatorDataHeader(data[FlagsOffset], BinaryPrimitives.ReadUInt32BigEndian(data[(FlagsOffset + 1)..]));
		return true;
	}
}
