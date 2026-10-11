using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Core.Security;

namespace Mokaterm.Core.Keys;

/// <summary>
/// The OpenSSH security key formats from PROTOCOL.u2f, in the field order <c>sshkey.c</c> writes them. The public
/// blob is the key type, the key and the application; the private blob repeats those and adds the flags, the key
/// handle and a reserved string, which is all an <c>sk</c> file keeps, since the private key stays on the device.
/// </summary>
internal static class SecurityKeyFile
{
	public const string CurveName = "nistp256";

	public const int Ed25519PublicKeyLength = 32;

	/// <summary>An uncompressed P-256 point: 0x04, then x and y.</summary>
	public const int EcPointLength = 65;

	/// <summary>CTAP2 caps a credential id at 1023 bytes, so a longer handle is not one.</summary>
	public const int MaxKeyHandleLength = 1023;

	/// <summary>Generous for reading: OpenSSH sets no limit, and only a hostile file comes near it.</summary>
	public const int MaxApplicationLength = 1024;

	private const int CheckWordsLength = 8;
	private const int CoordinateLength = 32;
	private const int MaxPaddingLength = 16;

	public static byte[] PublicBlob(SecurityKeyAlgorithm algorithm, ReadOnlySpan<byte> publicKey, string application)
	{
		ArrayBufferWriter<byte> writer = new(160);
		WritePublicFields(writer, algorithm, publicKey, application);
		return writer.WrittenSpan.ToArray();
	}

	/// <summary>The private half as it goes into the file's private section, between the check words and the comment.</summary>
	public static PinnedBytes PrivateBlob(
		SecurityKeyAlgorithm algorithm,
		ReadOnlySpan<byte> publicKey,
		string application,
		SecurityKeyFlags flags,
		ReadOnlySpan<byte> keyHandle)
	{
		using PinnedBufferWriter writer = new(512);
		WritePublicFields(writer, algorithm, publicKey, application);
		writer.GetSpan(1)[0] = (byte)flags;
		writer.Advance(1);
		SshWire.WriteString(writer, keyHandle);
		SshWire.WriteString(writer, ReadOnlySpan<byte>.Empty);
		return PinnedBytes.Copy(writer.WrittenSpan);
	}

	/// <summary>A P-256 point that is on the curve, or 32 bytes of Ed25519, which is all OpenSSH checks of one either.</summary>
	public static bool IsValidPublicKey(SecurityKeyAlgorithm algorithm, ReadOnlySpan<byte> publicKey)
	{
		if (algorithm == SecurityKeyAlgorithm.Ed25519)
		{
			return publicKey.Length == Ed25519PublicKeyLength;
		}

		if (publicKey.Length != EcPointLength || publicKey[0] != 0x04)
		{
			return false;
		}

		try
		{
			// Importing a named curve point checks that it lies on the curve.
			using ECDsa key = ECDsa.Create(new ECParameters
			{
				Curve = ECCurve.NamedCurves.nistP256,
				Q = new ECPoint
				{
					X = publicKey.Slice(1, CoordinateLength).ToArray(),
					Y = publicKey.Slice(1 + CoordinateLength, CoordinateLength).ToArray(),
				},
			});
			return true;
		}
		catch (Exception ex) when (ex is CryptographicException or PlatformNotSupportedException)
		{
			// Windows CNG reports a point off the curve as an unsupported curve, wrapping the real reason.
			return false;
		}
	}

	/// <summary>
	/// Reads a decrypted private section, check words included, and holds it against the public blob the container keeps
	/// in the clear: a file whose two halves disagree is refused, as <c>sshkey_parse_private2</c> refuses it.
	/// </summary>
	public static SecurityKeyFileResult Parse(ReadOnlySpan<byte> publicBlob, ReadOnlySpan<byte> section)
	{
		SshWireReader reader = new(section);
		if (!reader.TrySkip(CheckWordsLength) || !reader.TryReadString(out ReadOnlySpan<byte> typeName))
		{
			return Damaged();
		}

		string keyType = Encoding.UTF8.GetString(typeName);
		if (!SecurityKeyTypes.TryGetAlgorithm(keyType, out SecurityKeyAlgorithm algorithm))
		{
			return SecurityKeyTypes.IsSecurityKey(keyType)
				? SecurityKeyFileResult.Failed(PrivateKeyStatus.Unsupported, "Security key certificates cannot be used yet. Use the plain key file.")
				: SecurityKeyFileResult.Failed(PrivateKeyStatus.Invalid, "This is not a security key.");
		}

		if (algorithm == SecurityKeyAlgorithm.EcdsaP256
			&& (!reader.TryReadString(out ReadOnlySpan<byte> curve) || !curve.SequenceEqual(Encoding.ASCII.GetBytes(CurveName))))
		{
			return Damaged();
		}

		if (!reader.TryReadString(out ReadOnlySpan<byte> publicKey)
			|| !IsValidPublicKey(algorithm, publicKey)
			|| !reader.TryReadString(out ReadOnlySpan<byte> applicationBytes)
			|| applicationBytes.IsEmpty
			|| applicationBytes.Length > MaxApplicationLength
			|| applicationBytes.Contains((byte)0)
			|| !reader.TryReadByte(out byte flags)
			|| !reader.TryReadString(out ReadOnlySpan<byte> keyHandle)
			|| keyHandle.IsEmpty
			|| keyHandle.Length > MaxKeyHandleLength
			|| !reader.TryReadString(out _)
			|| !reader.TryReadString(out ReadOnlySpan<byte> commentBytes)
			|| !IsPadding(reader.Rest))
		{
			return Damaged();
		}

		string application = Encoding.UTF8.GetString(applicationBytes);
		if (!PublicBlob(algorithm, publicKey, application).AsSpan().SequenceEqual(publicBlob))
		{
			return SecurityKeyFileResult.Failed(PrivateKeyStatus.Invalid, "The private half of this file does not belong to its public key.");
		}

		return SecurityKeyFileResult.Valid(new SshSecurityKey
		{
			Algorithm = algorithm,
			PublicKeyBlob = publicBlob.ToArray(),
			Application = application,
			Flags = (SecurityKeyFlags)flags,
			KeyHandle = keyHandle.ToArray(),
			Comment = Encoding.UTF8.GetString(commentBytes),
		});
	}

	private static void WritePublicFields(IBufferWriter<byte> writer, SecurityKeyAlgorithm algorithm, ReadOnlySpan<byte> publicKey, string application)
	{
		SshWire.WriteString(writer, SecurityKeyTypes.KeyType(algorithm));
		if (algorithm == SecurityKeyAlgorithm.EcdsaP256)
		{
			SshWire.WriteString(writer, CurveName);
		}

		SshWire.WriteString(writer, publicKey);
		SshWire.WriteString(writer, application);
	}

	/// <summary>OpenSSH pads the section with 1, 2, 3 and so on up to its block size, and checks that it did.</summary>
	private static bool IsPadding(ReadOnlySpan<byte> rest)
	{
		if (rest.Length >= MaxPaddingLength)
		{
			return false;
		}

		for (int i = 0; i < rest.Length; i++)
		{
			if (rest[i] != (byte)(i + 1))
			{
				return false;
			}
		}

		return true;
	}

	private static SecurityKeyFileResult Damaged() =>
		SecurityKeyFileResult.Failed(PrivateKeyStatus.Invalid, "The security key file is damaged or incomplete.");
}
