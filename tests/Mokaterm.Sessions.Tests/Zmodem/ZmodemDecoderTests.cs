using System.Text;
using Mokaterm.Sessions.Zmodem;

namespace Mokaterm.Sessions.Tests.Zmodem;

/// <summary>
/// The byte-level decoder: partial frames across feeds, the noise it hands back, broken frames, and the cancel
/// sequence — everything a channel can do to a watcher.
/// </summary>
public sealed class ZmodemDecoderTests
{
	[Fact]
	public void Header_SplitAcrossFeeds_OneByteAtATime_StillParses()
	{
		byte[] sent = EncodeHex(ZmodemHeader.OurZrinit());
		ZmodemDecoder decoder = new();
		List<ZmodemReceived> events = [];

		foreach (byte value in sent)
		{
			decoder.Feed([value]);
			events.AddRange(decoder.Events);
		}

		// Each feed clears its own events, so what crossed the feeds is collected here.
		ZmodemReceived header = Assert.Single(events, received => received.Kind == ZmodemReceivedKind.Header);
		Assert.Equal(ZmodemCode.ZRINIT, header.Header.Type);
		Assert.Equal(ZmodemCode.OurCapabilities, header.Header.Capabilities);

		// Every byte of the frame was spoken for: none of it leaked back as terminal text.
		Assert.DoesNotContain(events, received => received.Kind == ZmodemReceivedKind.Noise);
	}

	[Fact]
	public void Header_WithoutItsLastTailByte_StaysOpenUntilMoreComes()
	{
		byte[] sent = EncodeHex(ZmodemHeader.OurZrinit());

		// Through the CR of the tail: the frame is open, waiting for the LF or whatever follows it.
		ZmodemDecoder waiting = new();
		waiting.Feed(sent.AsSpan(..^2));
		Assert.True(waiting.IsMidFrame);
		waiting.Feed([0x0a]);
		Assert.False(waiting.IsMidFrame);
		Assert.Equal(ZmodemCode.ZRINIT, Single(waiting, ZmodemReceivedKind.Header).Header.Type);
	}

	[Theory]
	[InlineData(new byte[] { 0x0d, 0x0a })]
	[InlineData(new byte[] { 0x0d })]
	[InlineData(new byte[] { 0x0a })]
	[InlineData(new byte[] { ZmodemCode.XON })]
	public void HexHeader_AcceptsEveryLegalTail(byte[] tail)
	{
		byte[] sent = [.. EncodeHex(new ZmodemHeader(ZmodemCode.ZRQINIT)).AsSpan(..^3).ToArray(), .. tail, .. "after"u8.ToArray()];
		ZmodemDecoder decoder = new();
		decoder.Feed(sent);

		Assert.Equal(ZmodemCode.ZRQINIT, Single(decoder, ZmodemReceivedKind.Header).Header.Type);

		// Whatever followed the tail is not part of the frame.
		Assert.Equal("after"u8.ToArray(), Noise(decoder));
	}

	[Fact]
	public void PlainText_ComesBackAsNoiseUnchanged()
	{
		string text = "user@host:~$ ls **\r\ntotal 0 *\x18\r\nrz waiting to receive.\r\n";
		ZmodemDecoder decoder = new();
		decoder.Feed(Encoding.UTF8.GetBytes(text));

		Assert.Equal(text, Encoding.UTF8.GetString(Noise(decoder)));
		// A failed frame attempt only marks the bytes; every one of them still comes back.
		Assert.DoesNotContain(decoder.Events, received => received.Kind is (ZmodemReceivedKind.Header or ZmodemReceivedKind.Packet or ZmodemReceivedKind.Canceled));
	}

	[Fact]
	public void BrokenHexFrame_ReturnsItsBytesAsNoiseAndFindsTheNextFrame()
	{
		byte[] good = EncodeHex(ZmodemHeader.OurZrinit());
		byte[] frame = EncodeHex(new ZmodemHeader(ZmodemCode.ZRQINIT));
		byte[] broken = [.. frame.AsSpan(..^1).ToArray()]; // without the XON, which a receiver silently drops
		broken[17] = 0x66; // The low nibble of the CRC's second byte: a frame check that cannot hold.

		ZmodemDecoder decoder = new();
		decoder.Feed([.. "text "u8.ToArray(), .. broken, .. good]);

		Assert.Equal(ZmodemCode.ZRINIT, Single(decoder, ZmodemReceivedKind.Header).Header.Type);
		Assert.Equal([.. "text "u8.ToArray(), .. broken], Noise(decoder));
	}

	[Fact]
	public void GarbledFrameKind_ReturnsItsBytesAsNoise()
	{
		byte[] almost = [.. "pre"u8.ToArray(), ZmodemCode.ZPAD, ZmodemCode.ZDLE, (byte)'D', .. "post"u8.ToArray()];
		ZmodemDecoder decoder = new();
		decoder.Feed(almost);

		Assert.Equal(almost, Noise(decoder));
	}

	[Fact]
	public void FiveCans_AreTheCancel_AndMoreTextAfterwardsIsNoise()
	{
		byte[] cans = [.. Enumerable.Repeat(ZmodemCode.Cancel, 5)];
		byte[] sent = [.. "ok"u8.ToArray(), .. cans, .. "back"u8.ToArray()];
		ZmodemDecoder decoder = new();
		decoder.Feed(sent);

		Assert.Contains(decoder.Events, received => received.Kind == ZmodemReceivedKind.Canceled);
		Assert.Equal("okback"u8.ToArray(), Noise(decoder));
	}

	[Fact]
	public void DataSubpackets_FlowOneAfterAnother_UntilAClosingEnd()
	{
		ZmodemHeaderEncoding encoding = ZmodemHeaderEncoding.Binary32;
		ZmodemEncoder encoder = new();
		encoder.Header(ZmodemHeader.OfPosition(ZmodemCode.ZDATA, 0, encoding), encoding);
		encoder.Subpacket("one"u8, ZmodemCode.ZCRCG, crc32: true);
		encoder.Subpacket("two"u8, ZmodemCode.ZCRCQ, crc32: true);
		encoder.Subpacket("three"u8, ZmodemCode.ZCRCE, crc32: true);

		ZmodemDecoder decoder = new();
		decoder.Feed(encoder.TakeOutput());

		ZmodemDataPacket[] packets = [.. decoder.Events
			.Where(received => received.Kind == ZmodemReceivedKind.Packet)
			.Select(received => received.Packet!)];
		Assert.Equal(3, packets.Length);
		Assert.Equal("one"u8.ToArray(), packets[0].Data);
		Assert.Equal("two"u8.ToArray(), packets[1].Data);
		Assert.Equal("three"u8.ToArray(), packets[2].Data);
		Assert.All(packets, packet => Assert.True(packet.Crc32));

		// ZCRCQ is an answer-me frame, ZCRCE closes the run; the session reads both off the packet's End.
		Assert.Equal(ZmodemCode.ZCRCQ, packets[1].End);
		Assert.Equal(ZmodemCode.ZCRCE, packets[2].End);
	}

	[Fact]
	public void CorruptedSubpacket_IsMalformed_NotDelivered()
	{
		ZmodemHeaderEncoding encoding = ZmodemHeaderEncoding.Binary32;
		ZmodemEncoder encoder = new();
		encoder.Header(ZmodemHeader.OfPosition(ZmodemCode.ZDATA, 0, encoding), encoding);
		encoder.Subpacket("payload"u8, ZmodemCode.ZCRCG, crc32: true);
		byte[] sent = encoder.TakeOutput();

		// Damage a payload byte after the frame was written; the check has to notice.
		int dataAt = sent.AsSpan().IndexOf("payload"u8);
		Assert.True(dataAt > 0);
		sent[dataAt] ^= 0x20;

		ZmodemDecoder decoder = new();
		decoder.Feed(sent);

		Assert.DoesNotContain(decoder.Events, received => received.Kind == ZmodemReceivedKind.Packet);
		Assert.Contains(decoder.Events, received => received.Kind == ZmodemReceivedKind.Malformed);
	}

	[Fact]
	public void XonAndXoffInsideADataStream_AreSkippedNotDecoded()
	{
		ZmodemHeaderEncoding encoding = ZmodemHeaderEncoding.Binary16;
		ZmodemEncoder encoder = new();
		encoder.Header(ZmodemHeader.OfPosition(ZmodemCode.ZDATA, 0, encoding), encoding);
		byte[] header = encoder.TakeOutput();
		encoder.Subpacket("a"u8, ZmodemCode.ZCRCG, crc32: false);
		byte[] subpacket = encoder.TakeOutput();

		// Line noise between the header and the subpacket must not reach the data.
		ZmodemDecoder decoder = new();
		decoder.Feed([.. header, ZmodemCode.XOFF, ZmodemCode.XON, .. subpacket]);

		ZmodemDataPacket packet = Single(decoder, ZmodemReceivedKind.Packet).Packet!;
		Assert.Equal("a"u8.ToArray(), packet.Data);
	}

	private static byte[] EncodeHex(ZmodemHeader header)
	{
		ZmodemEncoder encoder = new();
		encoder.Header(header, ZmodemHeaderEncoding.Hex);
		return encoder.TakeOutput();
	}

	private static ZmodemReceived Single(ZmodemDecoder decoder, ZmodemReceivedKind kind)
	{
		ZmodemReceived[] matches = [.. decoder.Events.Where(received => received.Kind == kind)];
		return Assert.Single(matches);
	}

	private static byte[] Noise(ZmodemDecoder decoder)
	{
		List<byte> noise = [.. decoder.Events.Where(received => received.Kind == ZmodemReceivedKind.Noise).Select(received => received.NoiseByte)];
		return [.. noise];
	}
}
