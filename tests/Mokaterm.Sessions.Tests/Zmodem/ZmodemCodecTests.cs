using System.Text;
using Mokaterm.Sessions.Zmodem;

namespace Mokaterm.Sessions.Tests.Zmodem;

/// <summary>The wire codec: CRCs, header encodings, escaping and the file information subpacket, against known bytes.</summary>
public sealed class ZmodemCodecTests
{
	[Fact]
	public void Crc16_MatchesTheUpdcrcConvention()
	{
		// lrzsz's updcrc is its own convention; the running value of the standard check string is BEEF for it.
		Assert.Equal(0xBEEF, ZmodemCrc.Running16("123456789"u8));

		// What goes on the wire is the running value finished with two zero bytes: for five zeros, zero.
		Assert.Equal(0x0000, ZmodemCrc.Crc16([0, 0, 0, 0, 0]));
	}

	[Fact]
	public void Crc32_FoldsItsOwnCheckBytesIntoTheConstant()
	{
		byte[] data = "123456789"u8.ToArray();
		uint check = ZmodemCrc.Transmit32(data);

		// A receiver starts where the sender starts and folds the four transmitted check bytes in, low byte first.
		uint crc = ZmodemCrc.Running32(data);
		crc = ZmodemCrc.Append32(crc, (byte)check);
		crc = ZmodemCrc.Append32(crc, (byte)(check >> 8));
		crc = ZmodemCrc.Append32(crc, (byte)(check >> 16));
		crc = ZmodemCrc.Append32(crc, (byte)(check >> 24));
		Assert.Equal(ZmodemCrc.Verified32, crc);
	}

	[Fact]
	public void HexHeader_FrameBytesMatchWhatLrzszSends()
	{
		// sz's invitation is five zero bytes, whose CRC-16 is zero: **^XB00000000000000, the frame zmodem traces show.
		Assert.Equal("2a 2a 18 42 30 30 30 30 30 30 30 30 30 30 30 30 30 30", FrameDigits(EncodeHex(new ZmodemHeader(ZmodemCode.ZRQINIT))));

		// rz's answer with lrzsz's capability set, digits taken from a real capture: **^XB0100000027fed4.
		ZmodemHeader lrzszZrinit = new(ZmodemCode.ZRINIT, 0, 0, 0, (byte)(ZmodemCode.CANBRK | ZmodemCode.CANFDX | ZmodemCode.CANOVIO | ZmodemCode.CANFC32));
		Assert.Equal("2a 2a 18 42 30 31 30 30 30 30 30 30 32 37 66 65 64 34", FrameDigits(EncodeHex(lrzszZrinit)));
	}

	[Fact]
	public void HexHeader_MokatermZrinit_CarriesHonestCapabilities()
	{
		// Full duplex, overlapped IO and 32-bit CRCs; no buffer limit and no escaping demands.
		byte[] sent = EncodeHex(ZmodemHeader.OurZrinit());
		Assert.Equal("2a 2a 18 42 30 31 30 30 30 30 30 30 32 33 62 65 35 30", FrameDigits(sent));
	}

	[Fact]
	public void HexHeader_ClosesWithCrLfXon_ExceptAfterZackAndZfin()
	{
		Assert.EndsWith("0d 0a 11", Hex(EncodeHex(new ZmodemHeader(ZmodemCode.ZRINIT))));
		Assert.EndsWith("0d 0a", Hex(EncodeHex(new ZmodemHeader(ZmodemCode.ZFIN))));
		Assert.EndsWith("0d 0a", Hex(EncodeHex(ZmodemHeader.OfPosition(ZmodemCode.ZACK, 5))));
	}

	[Fact]
	public void HexHeader_DecodesTheHighBitLineFeedThatLrzszSends()
	{
		byte[] sent = EncodeHex(ZmodemHeader.OurZrinit());
		sent[^2] = 0x0d;
		sent[^1] = 0x8a;

		ZmodemDecoder decoder = new();
		decoder.Feed(sent);
		Assert.Equal(ZmodemCode.ZRINIT, SingleHeader(decoder).Header.Type);
	}

	[Theory]
	[InlineData("2a 18 41", (int)ZmodemHeaderEncoding.Binary16)]
	[InlineData("2a 2a 18 43", (int)ZmodemHeaderEncoding.Binary32)]
	public void BinaryHeaders_UseTheProtocolPrefixes(string prefix, int encodingValue)
	{
		ZmodemHeaderEncoding encoding = (ZmodemHeaderEncoding)encodingValue;
		byte[] sent = Encode(new ZmodemHeader(ZmodemCode.ZRPOS, 0x04, 0x03, 0x02, 0x01, encoding), encoding);
		Assert.StartsWith(prefix, Hex(sent));

		ZmodemDecoder decoder = new();
		decoder.Feed(sent);
		ZmodemReceived header = SingleHeader(decoder);
		Assert.Equal(ZmodemCode.ZRPOS, header.Header.Type);

		// Positions travel least significant byte first, after the frame type.
		Assert.Equal(0x01020304, header.Header.Position);
	}

	[Fact]
	public void Subpacket_RoundTripsThroughTheDecoder_WithEitherCheck()
	{
		foreach (bool crc32 in new[] { false, true })
		{
			// Every byte the escaping has rules for: ZDLE, XON, XOFF, DLE, their high-bit copies, '@' CR, and the rubouts.
			byte[] data = [0x18, 0x11, 0x13, 0x10, 0x40, 0x0d, 0x41, 0x7f, 0xff, 0x00, 0x0d, 0x91, 0x90];
			ZmodemDataPacket packet = SendAndDecode(data, ZmodemCode.ZCRCW, crc32);
			Assert.Equal(data, packet.Data);
			Assert.Equal(ZmodemCode.ZCRCW, packet.End);
			Assert.Equal(crc32, packet.Crc32);
		}
	}

	[Fact]
	public void Subpacket_Zcrcw_EndsWithXonAndOthersDoNot()
	{
		Assert.Equal(ZmodemCode.XON, EncodeSubpacket("a"u8.ToArray(), ZmodemCode.ZCRCW, crc32: false)[^1]);
		Assert.NotEqual(ZmodemCode.XON, EncodeSubpacket("a"u8.ToArray(), ZmodemCode.ZCRCG, crc32: false)[^1]);
	}

	[Fact]
	public void Escaping_FollowsLrzsz_IncludingTheTelenetCrRule()
	{
		// A lone CR goes out raw; the CR after an '@' hides behind a ZDLE so a link cannot read it as an escape.
		byte[] lone = EncodeSubpacket([0x41, 0x0d], ZmodemCode.ZCRCG, crc32: false);
		Assert.Equal((byte)'\r', lone[1]);

		byte[] afterAt = EncodeSubpacket([0x40, 0x0d], ZmodemCode.ZCRCG, crc32: false);
		Assert.Equal(ZmodemCode.ZDLE, afterAt[1]);

		// The escaped CR is CR with bit 6 set, the same trick ZDLEE uses for ZDLE itself.
		Assert.Equal((byte)0x4D, afterAt[2]);
	}

	[Fact]
	public void CancelSequence_IsAZcanFrameThenEightCansAndBackspaces()
	{
		ZmodemEncoder encoder = new();
		encoder.CancelSequence();
		byte[] sent = encoder.TakeOutput();

		// The spec's insurance: eight CANs so the other side's five-CAN detector fires, then backspaces that erase them.
		Assert.Equal(Enumerable.Repeat((byte)8, 8), sent[^8..]);
		Assert.Equal(Enumerable.Repeat(ZmodemCode.Cancel, 8), sent[^16..^8]);
		Assert.EndsWith("0d 0a 11", Hex(sent.AsSpan(..^16)));

		ZmodemDecoder decoder = new();
		decoder.Feed(sent);
		Assert.Equal(ZmodemCode.ZCAN, SingleHeader(decoder).Header.Type);
		Assert.Contains(decoder.Events, received => received.Kind == ZmodemReceivedKind.Canceled);
	}

	[Fact]
	public void FileInfo_RoundTrips_AndPutsTimesAndModesInOctal()
	{
		DateTimeOffset modified = DateTimeOffset.FromUnixTimeSeconds(Octal(741));
		ZmodemFileInfo info = new("report.txt", 1234, modified, (int)Octal(100644));

		ZmodemFileInfo parsed = ZmodemFileInfo.Parse(info.ToSubpacket());

		Assert.Equal("report.txt", parsed.Name);
		Assert.Equal(1234, parsed.Length);
		Assert.Equal(modified, parsed.Modified);
		Assert.Equal((int)Octal(100644), parsed.Mode);
		Assert.Contains("1234 741 100644 ", Encoding.UTF8.GetString(info.ToSubpacket()), StringComparison.Ordinal);
	}

	[Fact]
	public void FileInfo_TakesWhatIsThere()
	{
		Assert.Equal(new ZmodemFileInfo("only-name", null, null, null), ZmodemFileInfo.Parse("only-name\0"u8));
		Assert.Equal(new ZmodemFileInfo("", null, null, null), ZmodemFileInfo.Parse("no separator"u8));
	}

	/// <summary>The octal numbers the protocol puts in its file information, written the way tests read them.</summary>
	private static long Octal(long digits) => Convert.ToInt64(digits.ToString(System.Globalization.CultureInfo.InvariantCulture), 8);

	private static byte[] EncodeHex(ZmodemHeader header) => Encode(header, ZmodemHeaderEncoding.Hex);

	private static byte[] Encode(ZmodemHeader header, ZmodemHeaderEncoding encoding)
	{
		ZmodemEncoder encoder = new();
		encoder.Header(header, encoding);
		return encoder.TakeOutput();
	}

	private static byte[] EncodeSubpacket(byte[] data, byte end, bool crc32)
	{
		ZmodemEncoder encoder = new();
		encoder.Subpacket(data, end, crc32);
		return encoder.TakeOutput();
	}

	private static ZmodemDataPacket SendAndDecode(byte[] data, byte end, bool crc32)
	{
		ZmodemHeaderEncoding encoding = crc32 ? ZmodemHeaderEncoding.Binary32 : ZmodemHeaderEncoding.Binary16;
		ZmodemEncoder encoder = new();
		encoder.Header(ZmodemHeader.OfPosition(ZmodemCode.ZDATA, 0, encoding), encoding);
		encoder.Subpacket(data, end, crc32);
		ZmodemDecoder decoder = new();
		decoder.Feed(encoder.TakeOutput());
		return SinglePacket(decoder).Packet!;
	}

	private static ZmodemReceived SingleHeader(ZmodemDecoder decoder) => SingleOf(decoder, ZmodemReceivedKind.Header);

	private static ZmodemReceived SinglePacket(ZmodemDecoder decoder) => SingleOf(decoder, ZmodemReceivedKind.Packet);

	private static ZmodemReceived SingleOf(ZmodemDecoder decoder, ZmodemReceivedKind kind)
	{
		ZmodemReceived[] matches = [.. decoder.Events.Where(received => received.Kind == kind)];
		return Assert.Single(matches);
	}

	/// <summary>The frame without its CR LF XON tail: what the digits and their CRC should be.</summary>
	private static string FrameDigits(byte[] frame) => Hex(frame.AsSpan(..^3));

	private static string Hex(ReadOnlySpan<byte> bytes) => string.Join(' ', bytes.ToArray().Select(b => b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
}
