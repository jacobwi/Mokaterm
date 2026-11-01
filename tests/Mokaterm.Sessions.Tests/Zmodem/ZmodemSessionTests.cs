using System.Text;
using Mokaterm.Sessions.Zmodem;

namespace Mokaterm.Sessions.Tests.Zmodem;

/// <summary>
/// Both flows of the pure session: a scripted sz offers and streams files in, a scripted rz invites ours out — with the
/// retransmits, skips, corruptions and cancels a real pair throws at it. The peer's bytes are built with the same encoder
/// the app uses, and the app's answers are decoded back with the same decoder, so every round trip crosses the wire twice.
/// </summary>
public sealed class ZmodemSessionTests
{
	private static readonly DateTimeOffset Modified = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

	private readonly ZmodemSession _session = new();
	private readonly List<byte> _wire = [];

	[Fact]
	public void Download_OfferAcceptStreamAndFinish()
	{
		byte[] file = [.. Repeat(0xA7, 2048)];

		Feed(PeerHex(ZmodemCode.ZRQINIT));
		AssertBytes("2a 2a 18 42 30 31 30 30 30 30 30 30 32 33 62 65 35 30 0d 0a 11", LastSent());

		Feed(PeerFile(new ZmodemFileInfo("log.txt", file.Length, Modified, (int)Octal(100644))));
		ZmodemFileInfo offer = Assert.IsType<ZmodemFileInfo>(_session.Offer);
		Assert.Equal("log.txt", offer.Name);
		Assert.Equal(file.Length, offer.Length);
		Assert.Equal(ZmodemSessionState.Offering, _session.State);

		ZmodemStep accept = _session.Accept();
		Collect(accept);
		Assert.Equal(ZmodemCode.ZRPOS, LastHeader().Type);
		Assert.Equal(0, LastHeader().Position);
		Assert.Equal(ZmodemSessionState.Receiving, _session.State);

		ZmodemStep streamed = Feed(PeerData(file, from: 0, corruptAt: null).ToArray());
		byte[] received = [.. streamed.FileData.SelectMany(chunk => chunk)];
		Assert.Equal<byte>(file, received);
		Assert.Equal(file.Length, _session.FileBytes);

		ZmodemStep finished = Feed(PeerHex(ZmodemCode.ZEOF, Position(file.Length)));
		Assert.True(finished.FileFinished);
		Assert.Equal(ZmodemCode.ZRINIT, LastHeader().Type);

		ZmodemStep done = Feed(PeerHex(ZmodemCode.ZFIN));
		Assert.True(done.Done);
		Assert.Null(done.FailedReason);
		Assert.Contains(ZmodemCode.ZFIN, Headers().Select(header => header.Type));

		// The peer's closing "OO" is swallowed; the terminal gets whatever follows it.
		ZmodemStep tail = Feed("OO~ $ "u8.ToArray());
		Assert.Equal("~ $ "u8.ToArray(), tail.Passthrough.ToArray());
		Assert.False(_session.Intercepts);
	}

	[Fact]
	public void Download_TwoFiles_OneSession()
	{
		StartDownload("one.bin", 512);
		Assert.True(_session.Accept().FileStarted);
		Feed(PeerData([.. Repeat(1, 512)], from: 0, corruptAt: null).ToArray());
		Feed(PeerHex(ZmodemCode.ZEOF, Position(512)));

		Feed(PeerFile(new ZmodemFileInfo("two.bin", 256, Modified, null)));
		Assert.Equal("two.bin", _session.Offer?.Name);
		Assert.True(_session.Accept().FileStarted);
		ZmodemStep second = Feed(PeerData([.. Repeat(2, 256)], from: 0, corruptAt: null).ToArray());
		Assert.Equal(256, second.FileData.Single().Length);

		Assert.True(Feed(PeerHex(ZmodemCode.ZEOF, Position(256))).FileFinished);
		Assert.True(Feed(PeerHex(ZmodemCode.ZFIN)).Done);
	}

	[Fact]
	public void Download_Skip_MovesThePeerToItsNextFile()
	{
		StartDownload("skipped.txt", 10);

		ZmodemStep skip = _session.Skip();
		Collect(skip);
		Assert.Equal(ZmodemCode.ZSKIP, LastHeader().Type);
		Assert.Equal(ZmodemSessionState.Negotiating, _session.State);

		Feed(PeerFile(new ZmodemFileInfo("kept.txt", 10, Modified, null)));
		Assert.Equal("kept.txt", _session.Offer?.Name);
	}

	[Fact]
	public void Download_CorruptedSubpacket_SendsThePeerBackToTheLastGoodByte()
	{
		byte[] file = Varied(2048);
		StartDownload("damaged.bin", file.Length);
		_session.Accept();

		// The first half arrives, the second arrives broken: the answer is a ZRPOS at the last good position.
		Feed(PeerData(file, from: 0, corruptAt: 1100).ToArray());
		Assert.Equal(ZmodemCode.ZRPOS, Headers().Last().Type);
		Assert.Equal(1024, Headers().Last().Position);

		ZmodemStep rest = Feed(PeerData(file[1024..], from: 1024, corruptAt: null).ToArray());
		Assert.Equal(file.Length - 1024, rest.FileData.Sum(chunk => chunk.Length));

		Assert.True(Feed(PeerHex(ZmodemCode.ZEOF, Position(file.Length))).FileFinished);
	}

	[Fact]
	public void Download_DataFromTheWrongPosition_IsRefusedUntilItMatches()
	{
		byte[] file = Varied(700);
		StartDownload("off.bin", file.Length);
		_session.Accept();

		// A ZDATA that claims to start past where the file here ends asks for a correction, and its data is dropped.
		ZmodemStep wrong = Feed(PeerData([.. Repeat(9, 100)], from: 500, corruptAt: null).ToArray());
		Assert.Empty(wrong.FileData);
		Assert.Equal(ZmodemCode.ZRPOS, Headers().Last().Type);

		ZmodemStep right = Feed(PeerData(file, from: 0, corruptAt: null).ToArray());
		Assert.Equal(file.Length, right.FileData.Sum(chunk => chunk.Length));
	}

	[Fact]
	public void Download_Cancel_PutsTheCancelSequenceOnTheWire()
	{
		StartDownload("any.txt", 100);
		_session.Accept();

		ZmodemStep cancel = _session.Cancel();
		Collect(cancel);

		Assert.True(cancel.Done);
		Assert.True(cancel.Aborted);
		byte[] sent = Sent();
		Assert.Equal(Enumerable.Repeat(ZmodemCode.Cancel, 8), sent[^16..^8]);
		Assert.Equal(Enumerable.Repeat((byte)8, 8), sent[^8..]);
		Assert.Contains(ZmodemCode.ZCAN, Headers().Select(header => header.Type));
	}

	[Fact]
	public void Download_PeerCans_EndTheSessionWithAReason()
	{
		StartDownload("any.txt", 100);
		_session.Accept();

		ZmodemStep done = Feed([.. Enumerable.Repeat(ZmodemCode.Cancel, 5)]);
		Assert.True(done.Done);
		Assert.Equal("The other side canceled the transfer.", done.FailedReason);
	}

	[Fact]
	public void Watch_PlainEchoOfRzAndAPrompt_IsOnlyTerminalText()
	{
		ZmodemStep step = Feed(Encoding.UTF8.GetBytes("user@host:~$ rz\r\nrz waiting to receive.\r\n"));

		Assert.Equal(ZmodemSessionState.Watching, _session.State);
		Assert.Equal("user@host:~$ rz\r\nrz waiting to receive.\r\n"u8.ToArray(), step.Passthrough.ToArray());
	}

	[Fact]
	public void Watch_EntryAfterTextOnTheLine_EngagesAndKeepsTheText()
	{
		ZmodemStep step = Feed([.. "ls\r\nfile1\r\n"u8.ToArray(), .. PeerHex(ZmodemCode.ZRQINIT)]);

		Assert.True(_session.Intercepts);
		Assert.Equal("ls\r\nfile1\r\n"u8.ToArray(), step.Passthrough.ToArray());
		Assert.Equal(ZmodemCode.ZRINIT, LastHeader().Type);
	}

	[Fact]
	public void Watch_BrokenFrameCheck_DoesNotEngage_AndReturnsTheBytes()
	{
		byte[] frame = PeerHex(ZmodemCode.ZRQINIT);
		byte[] broken = [.. frame.AsSpan(..^1).ToArray()]; // without the XON, which a receiver silently drops
		broken[17] = 0x66;

		ZmodemStep step = Feed(broken);

		Assert.Equal(ZmodemSessionState.Watching, _session.State);
		Assert.Equal(broken, step.Passthrough.ToArray());
	}

	[Fact]
	public void Watch_AfterAFinishedSession_AFreshOneWatchesAgain()
	{
		StartDownload("again.txt", 4);
		_session.Accept();
		Feed(PeerData([1, 2, 3, 4], from: 0, corruptAt: null).ToArray());
		Assert.True(Feed(PeerHex(ZmodemCode.ZEOF, Position(4))).FileFinished);
		Assert.True(Feed(PeerHex(ZmodemCode.ZFIN)).Done);

		// Output between sessions reaches the terminal untouched, and a fresh session answers a new invitation.
		ZmodemStep between = Feed("$ "u8.ToArray());
		Assert.Equal("$ "u8.ToArray(), between.Passthrough.ToArray());

		ZmodemSession next = new();
		ZmodemStep invited = next.Feed(PeerHex(ZmodemCode.ZRQINIT));
		Assert.True(next.Intercepts);
		ZmodemDecoder fresh = new();
		fresh.Feed([.. invited.Send]);
		ZmodemReceived answer = Assert.Single(fresh.Events, received => received.Kind == ZmodemReceivedKind.Header);
		Assert.Equal(ZmodemCode.ZRINIT, answer.Header.Type);
	}

	[Fact]
	public void Upload_SendsAFileWithTheChecksThePeerAskedFor()
	{
		byte[] file = [.. Repeat(0xC3, 2048)];

		// rz's invitation, with its own capability set: our sender asks for the files and uses 32-bit checks.
		Feed(PeerHex(ZmodemCode.ZRINIT, flags: ZmodemCode.CANFDX | ZmodemCode.CANOVIO | ZmodemCode.CANFC32));
		Assert.Equal(ZmodemSessionState.ChoosingFiles, _session.State);

		ZmodemStep started = _session.StartUpload([new ZmodemFileInfo("out.bin", file.Length, Modified, null)]);
		Collect(started);
		Assert.Equal(ZmodemCode.ZFILE, Headers().Single().Type);
		ZmodemDataPacket info = Packets().Single();

		// The mode travels as 0: this side does not know one, and the peer writes what its own umask allows.
		Assert.Equal(new ZmodemFileInfo("out.bin", file.Length, Modified, 0), ZmodemFileInfo.Parse(info.Data));
		Assert.True(info.Crc32);

		ZmodemStep go = Feed(PeerHex(ZmodemCode.ZRPOS, Position(0)));
		Assert.True(go.DataWanted);
		Assert.Equal(0, go.DataPosition);
		Assert.True(go.FileStarted);

		ZmodemStep sent = _session.SupplyData(file);
		Collect(sent);
		Assert.Equal([.. Repeat(0xC3, 1024)], Packets()[1].Data);
		Assert.Equal(3, Packets().Count);
		Assert.Equal(ZmodemCode.ZCRCG, Packets()[1].End);
		Assert.Equal(ZmodemCode.ZCRCE, Packets()[2].End);
		Assert.Equal(ZmodemCode.ZDATA, Headers()[1].Type);
		Assert.Equal(0, Headers()[1].Position);
		Assert.Equal(ZmodemCode.ZEOF, Headers().Last().Type);
		Assert.Equal(file.Length, Headers().Last().Position);
		Assert.True(sent.FileFinished);

		ZmodemStep done = Feed(PeerHex(ZmodemCode.ZRINIT));
		Assert.True(done.Done);
		Assert.Contains(ZmodemCode.ZFIN, Headers().Select(header => header.Type));
	}

	[Fact]
	public void Upload_PeerWithoutCrc32_GetsBinary16Frames()
	{
		Feed(PeerHex(ZmodemCode.ZRINIT, flags: ZmodemCode.CANFDX));
		Collect(_session.StartUpload([new ZmodemFileInfo("small.txt", 5, Modified, null)]));

		Feed(PeerHex(ZmodemCode.ZRPOS, Position(0)));
		Collect(_session.SupplyData("hello"u8.ToArray()));

		Assert.All(Packets(), packet => Assert.False(packet.Crc32));
		Assert.Equal("hello"u8.ToArray(), Packets().Last().Data);
	}

	[Fact]
	public void Upload_TwoFiles_TheSecondFollowsTheFirstReceipt()
	{
		Feed(PeerHex(ZmodemCode.ZRINIT, flags: ZmodemCode.CANFC32));
		Collect(_session.StartUpload([
			new ZmodemFileInfo("a.txt", 3, Modified, null),
			new ZmodemFileInfo("b.txt", 4, Modified, null),
		]));

		Feed(PeerHex(ZmodemCode.ZRPOS, Position(0)));
		Collect(_session.SupplyData("aaa"u8.ToArray()));
		Assert.True(Feed(PeerHex(ZmodemCode.ZRINIT)).FileStarted);

		Feed(PeerHex(ZmodemCode.ZRPOS, Position(0)));
		ZmodemStep second = _session.SupplyData("bbbb"u8.ToArray());
		Collect(second);
		Assert.Equal("b.txt", _session.FileName);
		Assert.Equal("bbbb"u8.ToArray(), Packets().Last().Data);

		Assert.True(Feed(PeerHex(ZmodemCode.ZRINIT)).Done);
	}

	[Fact]
	public void Upload_PeerSkipsAFile_TheNextOneIsSent()
	{
		Feed(PeerHex(ZmodemCode.ZRINIT, flags: ZmodemCode.CANFC32));
		Collect(_session.StartUpload([
			new ZmodemFileInfo("a.txt", 3, Modified, null),
			new ZmodemFileInfo("b.txt", 4, Modified, null),
		]));

		ZmodemStep skipped = Feed(PeerHex(ZmodemCode.ZSKIP));
		Assert.True(skipped.FileStarted);
		Assert.Equal("b.txt", _session.CurrentOutbound?.Name);
	}

	[Fact]
	public void Upload_MidStreamZrpos_StartsOverFromWhereThePeerAsked()
	{
		Feed(PeerHex(ZmodemCode.ZRINIT, flags: ZmodemCode.CANFC32));
		Collect(_session.StartUpload([new ZmodemFileInfo("over.bin", 4096, Modified, null)]));

		Feed(PeerHex(ZmodemCode.ZRPOS, Position(0)));
		Collect(_session.SupplyData([.. Repeat(7, 4096)]));

		ZmodemStep again = Feed(PeerHex(ZmodemCode.ZRPOS, Position(1024)));
		Assert.True(again.DataWanted);
		Assert.Equal(1024, again.DataPosition);

		Collect(_session.SupplyData([.. Repeat(7, 3072)]));
		Assert.Equal(1024, Headers().Last(header => header.Type == ZmodemCode.ZDATA).Position);
	}

	[Fact]
	public void Upload_EmptyFile_EndsWithZEOFTheMomentThePeerAllowsIt()
	{
		Feed(PeerHex(ZmodemCode.ZRINIT, flags: ZmodemCode.CANFC32));
		Collect(_session.StartUpload([new ZmodemFileInfo("empty.txt", 0, Modified, null)]));

		ZmodemStep go = Feed(PeerHex(ZmodemCode.ZRPOS, Position(0)));
		Assert.False(go.DataWanted);
		Assert.True(go.FileFinished);
		Assert.Equal(ZmodemCode.ZEOF, Headers().Last().Type);
		Assert.Equal(0, Headers().Last().Position);
	}

	[Fact]
	public void Upload_NothingChosen_ClosesTheSession()
	{
		Feed(PeerHex(ZmodemCode.ZRINIT, flags: ZmodemCode.CANFC32));

		ZmodemStep none = _session.StartUpload([]);
		Collect(none);

		Assert.True(none.Done);
		Assert.Contains(ZmodemCode.ZFIN, Headers().Select(header => header.Type));
	}

	[Fact]
	public void Upload_SourceDrierThanItsStatedLength_CancelsHonestly()
	{
		Feed(PeerHex(ZmodemCode.ZRINIT, flags: ZmodemCode.CANFC32));
		Collect(_session.StartUpload([new ZmodemFileInfo("short.bin", 100, Modified, null)]));
		Feed(PeerHex(ZmodemCode.ZRPOS, Position(0)));

		ZmodemStep starved = _session.SupplyData([]);
		Assert.True(starved.Done);
		Assert.Equal("The file to send is shorter than it said it was.", starved.FailedReason);
	}

	private static byte[] Repeat(byte value, int count) => [.. Enumerable.Repeat(value, count)];

	/// <summary>A file whose runs of bytes are unique, so a test can name the eight bytes it means.</summary>
	private static byte[] Varied(int count) => [.. Enumerable.Range(0, count).Select(index => (byte)(index / 8))];

	private ZmodemStep Feed(params byte[][] chunks) => Feed([.. chunks.SelectMany(chunk => chunk)]);

	private ZmodemStep Feed(byte[] bytes)
	{
		ZmodemStep step = _session.Feed(bytes);
		Collect(step);
		return step;
	}

	private void Collect(ZmodemStep step) => _wire.AddRange(step.Send);

	private byte[] Sent() => [.. _wire];

	private byte[] LastSent() => [.. _wire.ToArray()[^21..]];

	private List<ZmodemHeader> Headers()
	{
		ZmodemDecoder decoder = new();
		decoder.Feed(Sent());
		return [.. decoder.Events.Where(received => received.Kind == ZmodemReceivedKind.Header).Select(received => received.Header)];
	}

	private List<ZmodemDataPacket> Packets()
	{
		ZmodemDecoder decoder = new();
		decoder.Feed(Sent());
		return [.. decoder.Events.Where(received => received.Kind == ZmodemReceivedKind.Packet).Select(received => received.Packet!)];
	}

	private ZmodemHeader LastHeader() => Headers()[^1];

	/// <summary>A hex header the way the peer sends its control frames.</summary>
	private static byte[] PeerHex(byte type, long position = 0, byte flags = 0)
	{
		ZmodemHeader header = type == ZmodemCode.ZRINIT
			? new ZmodemHeader(type, 0, 0, 0, flags)
			: ZmodemHeader.OfPosition(type, position);
		ZmodemEncoder encoder = new();
		encoder.Header(header, ZmodemHeaderEncoding.Hex);
		return encoder.TakeOutput();
	}

	/// <summary>The peer's ZFILE with its file information subpacket, as sz sends it: binary with 32-bit checks.</summary>
	private static byte[] PeerFile(ZmodemFileInfo info)
	{
		ZmodemEncoder encoder = new();
		encoder.Header(new ZmodemHeader(ZmodemCode.ZFILE, ZmodemCode.ZCBIN, 0, 0, 0, ZmodemHeaderEncoding.Binary32), ZmodemHeaderEncoding.Binary32);
		encoder.Subpacket(info.ToSubpacket(), ZmodemCode.ZCRCW, crc32: true);
		return encoder.TakeOutput();
	}

	/// <summary>A ZDATA stream that claims to start at <paramref name="from"/> and carries the given bytes in 1024-byte
	/// subpackets, the last of which closes with ZCRCE; a byte at <paramref name="corruptAt"/> is damaged on the wire.</summary>
	private static List<byte[]> PeerData(byte[] file, int from, int? corruptAt)
	{
		ZmodemEncoder encoder = new();
		encoder.Header(ZmodemHeader.OfPosition(ZmodemCode.ZDATA, from, ZmodemHeaderEncoding.Binary32), ZmodemHeaderEncoding.Binary32);
		for (int offset = 0; offset < file.Length; offset += 1024)
		{
			int length = Math.Min(1024, file.Length - offset);
			byte[] chunk = file[offset..(offset + length)];
			bool last = offset + length >= file.Length;
			encoder.Subpacket(chunk.AsSpan(), last ? ZmodemCode.ZCRCE : ZmodemCode.ZCRCG, crc32: true);
		}

		byte[] stream = encoder.TakeOutput();
		if (corruptAt is { } at)
		{
			// The corruption lands inside a payload byte, past the position it corrupts.
			int wireAt = stream.AsSpan().IndexOf(file.AsSpan(at, 8));
			Assert.True(wireAt > 0);
			stream[wireAt + 3] ^= 0x40;
		}

		return [stream];
	}

	/// <summary>Serves an offer without touching the wire assertions: the peer invites, offers, and stands by.</summary>
	private void StartDownload(string name, long length)
	{
		Feed(PeerHex(ZmodemCode.ZRQINIT));
		Feed(PeerFile(new ZmodemFileInfo(name, length, Modified, (int)Octal(100644))));
		Assert.Equal(ZmodemSessionState.Offering, _session.State);
	}

	private static long Position(long value) => value;

	/// <summary>The octal mode sz sends, written the way the test reads it.</summary>
	private static long Octal(long digits) => Convert.ToInt64(digits.ToString(System.Globalization.CultureInfo.InvariantCulture), 8);

	private static void AssertBytes(string hex, byte[] actual)
	{
		byte[] expected = [.. hex.Split(' ').Select(part => Convert.ToByte(part, 16))];
		Assert.Equal(expected, actual);
	}
}
