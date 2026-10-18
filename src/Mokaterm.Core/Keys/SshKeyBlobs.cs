using System.Buffers;

namespace Mokaterm.Core.Keys;

/// <summary>
/// The public key blobs and the private key fields OpenSSH writes into its key container, for every key Core builds:
/// generated keys and keys converted from PuTTY or PEM files. The private half lists the fields in OpenSSH's order,
/// which is not the order of the public half.
/// </summary>
internal static class SshKeyBlobs
{
	public const string Rsa = "ssh-rsa";

	public const string Ed25519 = "ssh-ed25519";

	public const int Ed25519KeyLength = 32;

	/// <summary><c>string "ssh-rsa", mpint e, mpint n</c>.</summary>
	public static void WriteRsaPublic(IBufferWriter<byte> writer, ReadOnlySpan<byte> exponent, ReadOnlySpan<byte> modulus)
	{
		SshWire.WriteString(writer, Rsa);
		SshWire.WriteMpInt(writer, exponent);
		SshWire.WriteMpInt(writer, modulus);
	}

	/// <summary><c>string "ssh-rsa", mpint n, e, d, iqmp, p, q</c>, where iqmp is the inverse of q modulo p.</summary>
	public static void WriteRsaPrivate(
		IBufferWriter<byte> writer,
		ReadOnlySpan<byte> modulus,
		ReadOnlySpan<byte> exponent,
		ReadOnlySpan<byte> privateExponent,
		ReadOnlySpan<byte> inverseQ,
		ReadOnlySpan<byte> p,
		ReadOnlySpan<byte> q)
	{
		SshWire.WriteString(writer, Rsa);
		SshWire.WriteMpInt(writer, modulus);
		SshWire.WriteMpInt(writer, exponent);
		SshWire.WriteMpInt(writer, privateExponent);
		SshWire.WriteMpInt(writer, inverseQ);
		SshWire.WriteMpInt(writer, p);
		SshWire.WriteMpInt(writer, q);
	}

	/// <summary><c>string "ssh-ed25519", string public key</c>.</summary>
	public static void WriteEd25519Public(IBufferWriter<byte> writer, ReadOnlySpan<byte> publicKey)
	{
		SshWire.WriteString(writer, Ed25519);
		SshWire.WriteString(writer, publicKey);
	}

	/// <summary>
	/// <c>string "ssh-ed25519", string public key, string seed || public key</c>: OpenSSH stores the 32 byte seed and the
	/// public key together as the 64 byte "secret key".
	/// </summary>
	public static void WriteEd25519Private(IBufferWriter<byte> writer, ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> seed)
	{
		SshWire.WriteString(writer, Ed25519);
		SshWire.WriteString(writer, publicKey);
		SshWire.WriteUInt32(writer, (uint)(seed.Length + publicKey.Length));
		SshWire.WriteBytes(writer, seed);
		SshWire.WriteBytes(writer, publicKey);
	}

	/// <summary><c>string "ecdsa-sha2-...", string curve, string Q, mpint d</c>.</summary>
	public static void WriteEcdsaPrivate(IBufferWriter<byte> writer, string algorithm, string curve, ReadOnlySpan<byte> point, ReadOnlySpan<byte> privateKey)
	{
		SshWire.WriteString(writer, algorithm);
		SshWire.WriteString(writer, curve);
		SshWire.WriteString(writer, point);
		SshWire.WriteMpInt(writer, privateKey);
	}
}
