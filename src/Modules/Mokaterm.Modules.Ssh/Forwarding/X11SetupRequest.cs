using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Mokaterm.Modules.Ssh.Forwarding;

/// <summary>
/// The connection setup an X client sends before anything else (X Window System Protocol, "Connection Setup"): a byte
/// order byte, the protocol version, then the authorization protocol name and data, each padded to a multiple of four.
/// Its lengths are in the client's own byte order, <c>B</c> for most significant byte first and <c>l</c> for least.
/// This is where forwarding checks the fake cookie the server's programs were given and swaps in the real one, which is
/// what <c>ssh -X</c> does.
/// </summary>
internal readonly record struct X11SetupRequest(bool BigEndian, int NameLength, int DataLength)
{
	public const string MitMagicCookie = "MIT-MAGIC-COOKIE-1";

	public const int HeaderLength = 12;

	/// <summary>The largest setup there can be: both lengths are 16 bits.</summary>
	public const int MaxLength = HeaderLength + (2 * (ushort.MaxValue + 1));

	private const byte MostSignificantFirst = (byte)'B';

	private const byte LeastSignificantFirst = (byte)'l';

	private const int NameLengthOffset = 6;

	private const int DataLengthOffset = 8;

	private static ReadOnlySpan<byte> MitMagicCookieBytes => "MIT-MAGIC-COOKIE-1"u8;

	/// <summary>Where the authorization data starts.</summary>
	public int DataOffset => HeaderLength + Pad(NameLength);

	/// <summary>The whole setup, padding included. Anything after it is the client's first request.</summary>
	public int Length => DataOffset + Pad(DataLength);

	/// <summary>Reads the setup at the start of <paramref name="buffer"/>, which may hold more than the setup.</summary>
	public static X11SetupStatus TryRead(ReadOnlySpan<byte> buffer, out X11SetupRequest request)
	{
		request = default;
		if (buffer.IsEmpty)
		{
			return X11SetupStatus.NeedMore;
		}

		if (buffer[0] is not (MostSignificantFirst or LeastSignificantFirst))
		{
			return X11SetupStatus.Invalid;
		}

		if (buffer.Length < HeaderLength)
		{
			return X11SetupStatus.NeedMore;
		}

		bool bigEndian = buffer[0] == MostSignificantFirst;
		request = new X11SetupRequest(
			bigEndian,
			ReadLength(buffer[NameLengthOffset..], bigEndian),
			ReadLength(buffer[DataLengthOffset..], bigEndian));
		return buffer.Length < request.Length ? X11SetupStatus.NeedMore : X11SetupStatus.Complete;
	}

	public ReadOnlySpan<byte> Name(ReadOnlySpan<byte> setup) => setup.Slice(HeaderLength, NameLength);

	public ReadOnlySpan<byte> Data(ReadOnlySpan<byte> setup) => setup.Slice(DataOffset, DataLength);

	/// <summary>
	/// True when the client authenticates with MIT-MAGIC-COOKIE-1 and exactly <paramref name="cookie"/>. The compare takes
	/// the same time wherever the bytes differ, so a client cannot learn the cookie a byte at a time.
	/// </summary>
	public bool Carries(ReadOnlySpan<byte> setup, ReadOnlySpan<byte> cookie) =>
		Name(setup).SequenceEqual(MitMagicCookieBytes)
		&& DataLength == cookie.Length
		&& CryptographicOperations.FixedTimeEquals(Data(setup), cookie);

	/// <summary>
	/// The setup with its authorization replaced by <paramref name="protocol"/> and <paramref name="data"/>, both empty
	/// for none. The version and the byte order stay the client's.
	/// </summary>
	public byte[] WithAuthorization(ReadOnlySpan<byte> setup, string protocol, ReadOnlySpan<byte> data)
	{
		ArgumentNullException.ThrowIfNull(protocol);
		byte[] name = Encoding.ASCII.GetBytes(protocol);
		if (name.Length > ushort.MaxValue || data.Length > ushort.MaxValue)
		{
			throw new ArgumentOutOfRangeException(nameof(data), "An X11 authorization is at most 65535 bytes.");
		}

		X11SetupRequest rewritten = this with { NameLength = name.Length, DataLength = data.Length };
		byte[] result = new byte[rewritten.Length];
		setup[..HeaderLength].CopyTo(result);
		WriteLength(result.AsSpan(NameLengthOffset), name.Length, BigEndian);
		WriteLength(result.AsSpan(DataLengthOffset), data.Length, BigEndian);
		name.CopyTo(result.AsSpan(HeaderLength));
		data.CopyTo(result.AsSpan(rewritten.DataOffset));
		return result;
	}

	private static int Pad(int length) => (length + 3) & ~3;

	private static int ReadLength(ReadOnlySpan<byte> bytes, bool bigEndian) =>
		bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(bytes) : BinaryPrimitives.ReadUInt16LittleEndian(bytes);

	private static void WriteLength(Span<byte> bytes, int length, bool bigEndian)
	{
		if (bigEndian)
		{
			BinaryPrimitives.WriteUInt16BigEndian(bytes, (ushort)length);
		}
		else
		{
			BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)length);
		}
	}
}
