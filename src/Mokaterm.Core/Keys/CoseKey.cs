using System.Buffers.Binary;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Security;

namespace Mokaterm.Core.Keys;

/// <summary>
/// The COSE_Key a new FIDO credential reports its public key in (RFC 9053), read with just enough CBOR for it: a map of
/// integer labels to integers and byte strings. Anything else in the map is skipped, nesting is capped, and indefinite
/// lengths are refused, since CTAP2 requires its canonical encoding anyway.
/// </summary>
internal static class CoseKey
{
	private const int MajorUnsigned = 0;
	private const int MajorNegative = 1;
	private const int MajorBytes = 2;
	private const int MajorText = 3;
	private const int MajorArray = 4;
	private const int MajorMap = 5;
	private const int MajorTag = 6;

	private const long LabelKeyType = 1;
	private const long LabelAlgorithm = 3;
	private const long LabelCurve = -1;
	private const long LabelX = -2;
	private const long LabelY = -3;

	private const long KeyTypeOkp = 1;
	private const long KeyTypeEc2 = 2;
	private const long AlgorithmEs256 = -7;
	private const long AlgorithmEdDsa = -8;
	private const long CurveP256 = 1;
	private const long CurveEd25519 = 6;

	private const int CoordinateLength = 32;
	private const int MaxEntries = 32;
	private const int MaxDepth = 8;

	/// <summary>The public key in the form an SSH key blob carries it: an uncompressed point, or 32 bytes of Ed25519.</summary>
	/// <exception cref="SecurityKeyException">The data is not a COSE key of the expected algorithm.</exception>
	public static byte[] ReadPublicKey(ReadOnlySpan<byte> cbor, SecurityKeyAlgorithm expected)
	{
		int position = 0;
		(int major, ulong entries) = ReadHeader(cbor, ref position);
		if (major != MajorMap || entries > MaxEntries)
		{
			throw Unreadable();
		}

		long? keyType = null;
		long? algorithm = null;
		long? curve = null;
		byte[]? x = null;
		byte[]? y = null;
		for (ulong i = 0; i < entries; i++)
		{
			switch (ReadLabel(cbor, ref position))
			{
				case LabelKeyType:
					keyType = ReadInteger(cbor, ref position);
					break;
				case LabelAlgorithm:
					algorithm = ReadInteger(cbor, ref position);
					break;
				case LabelCurve:
					curve = ReadInteger(cbor, ref position);
					break;
				case LabelX:
					x = ReadBytes(cbor, ref position).ToArray();
					break;
				case LabelY:
					y = ReadBytes(cbor, ref position).ToArray();
					break;
				default:
					Skip(cbor, ref position, 0);
					break;
			}
		}

		return expected switch
		{
			SecurityKeyAlgorithm.EcdsaP256 when keyType == KeyTypeEc2
				&& algorithm is null or AlgorithmEs256
				&& curve == CurveP256
				&& x is { Length: CoordinateLength }
				&& y is { Length: CoordinateLength } => [0x04, .. x, .. y],
			SecurityKeyAlgorithm.Ed25519 when keyType == KeyTypeOkp
				&& algorithm is null or AlgorithmEdDsa
				&& curve == CurveEd25519
				&& x is { Length: SecurityKeyFile.Ed25519PublicKeyLength } => x,
			_ => throw new SecurityKeyException(
				SecurityKeyFailure.NotSupported,
				$"The security key made a different kind of key than the {(expected == SecurityKeyAlgorithm.Ed25519 ? "Ed25519" : "ECDSA")} key that was asked for."),
		};
	}

	private static (int Major, ulong Value) ReadHeader(ReadOnlySpan<byte> cbor, ref int position)
	{
		if (position >= cbor.Length)
		{
			throw Unreadable();
		}

		byte initial = cbor[position++];
		int major = initial >> 5;
		int info = initial & 0x1F;
		int size = info switch
		{
			< 24 => 0,
			24 => 1,
			25 => 2,
			26 => 4,
			27 => 8,
			_ => throw Unreadable(),
		};

		if (size == 0)
		{
			return (major, (ulong)info);
		}

		if (cbor.Length - position < size)
		{
			throw Unreadable();
		}

		ReadOnlySpan<byte> argument = cbor.Slice(position, size);
		position += size;
		ulong value = size switch
		{
			1 => argument[0],
			2 => BinaryPrimitives.ReadUInt16BigEndian(argument),
			4 => BinaryPrimitives.ReadUInt32BigEndian(argument),
			_ => BinaryPrimitives.ReadUInt64BigEndian(argument),
		};

		return (major, value);
	}

	/// <summary>An integer label, or null after skipping a label of any other kind, which COSE allows and nothing here uses.</summary>
	private static long? ReadLabel(ReadOnlySpan<byte> cbor, ref int position)
	{
		if (position < cbor.Length && (cbor[position] >> 5) is MajorUnsigned or MajorNegative)
		{
			return ReadInteger(cbor, ref position);
		}

		Skip(cbor, ref position, 0);
		return null;
	}

	private static long ReadInteger(ReadOnlySpan<byte> cbor, ref int position)
	{
		(int major, ulong value) = ReadHeader(cbor, ref position);
		if (value > long.MaxValue)
		{
			throw Unreadable();
		}

		return major switch
		{
			MajorUnsigned => (long)value,
			MajorNegative => -1 - (long)value,
			_ => throw Unreadable(),
		};
	}

	private static ReadOnlySpan<byte> ReadBytes(ReadOnlySpan<byte> cbor, ref int position)
	{
		(int major, ulong length) = ReadHeader(cbor, ref position);
		if (major != MajorBytes || length > (ulong)(cbor.Length - position))
		{
			throw Unreadable();
		}

		ReadOnlySpan<byte> value = cbor.Slice(position, (int)length);
		position += (int)length;
		return value;
	}

	private static void Skip(ReadOnlySpan<byte> cbor, ref int position, int depth)
	{
		if (depth > MaxDepth)
		{
			throw Unreadable();
		}

		(int major, ulong value) = ReadHeader(cbor, ref position);
		switch (major)
		{
			case MajorBytes or MajorText:
				if (value > (ulong)(cbor.Length - position))
				{
					throw Unreadable();
				}

				position += (int)value;
				break;
			case MajorArray or MajorMap:
				ulong items = major == MajorMap ? value * 2 : value;
				if (value > MaxEntries)
				{
					throw Unreadable();
				}

				for (ulong i = 0; i < items; i++)
				{
					Skip(cbor, ref position, depth + 1);
				}

				break;
			case MajorTag:
				Skip(cbor, ref position, depth + 1);
				break;
		}
	}

	private static SecurityKeyException Unreadable() =>
		new(SecurityKeyFailure.Failed, "The security key sent a public key that could not be read.");
}
