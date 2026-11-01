using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Sessions.Terminal;
using Mokaterm.Sessions.Tests.Fakes;
using Mokaterm.Sessions.Zmodem;

namespace Mokaterm.Sessions.Tests;

/// <summary>
/// The whole chain over a real terminal stream: the pump reads a scripted sz, the transfer takes the channel over, the
/// terminal keeps only plain text, and the save lands in the fake host. Everything the pure session tests cannot say
/// about the stream's own gates and threads.
/// </summary>
public sealed class TerminalZmodemTests : IAsyncDisposable
{
	private static readonly TerminalSize Size80X24 = new(80, 24);

	private readonly List<TerminalStream> _streams = [];
	private readonly FakeLocalFileAccess _localFiles = new();
	private readonly FakeTimeProvider _time = new();

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Download_ThroughThePump_SavesTheFileAndKeepsTheTerminalClean()
	{
		byte[] file = [.. Enumerable.Repeat((byte)'z', 2048)];
		(TerminalStream Stream, FakeTerminalChannel Channel, RecordingSink Sink) harness = Create();
		FakeTerminalChannel channel = harness.Channel;
		RecordingSink sink = harness.Sink;
		TerminalStream stream = harness.Stream;

		channel.Emit("$ sz log.bin\r\n");
		channel.Emit(PeerHex(ZmodemCode.ZRQINIT));
		await Eventually.TrueAsync(() => SentHeaders(channel).Any(header => header.Type == ZmodemCode.ZRINIT), "the transfer answers the invitation");

		channel.Emit(PeerFile(new ZmodemFileInfo("log.bin", file.Length, DateTimeOffset.FromUnixTimeSeconds(5), (int)Octal(100644))));
		await Eventually.TrueAsync(() => stream.Zmodem?.Phase == ZmodemPhase.Offering, "the offer reaches the view");
		ZmodemFileOffer? offer = stream.Zmodem?.Offer;
		Assert.NotNull(offer);
		Assert.Equal("log.bin", offer.FileName);
		Assert.Equal(2048L, offer.Length);

		// The terminal got the shell line but none of the frame, and typing now goes nowhere.
		await Eventually.TrueAsync(() => sink.Text.StartsWith("$ sz log.bin\r\n", StringComparison.Ordinal), "the shell line survives");
		Assert.DoesNotContain("\u0018B", sink.Text, StringComparison.Ordinal);
		await stream.SendTextAsync("quit", Ct);
		await Eventually.StaysTrueAsync(() => !channel.InputText.Contains("quit", StringComparison.Ordinal), "typing while transferring is dropped");

		// The save is what tells the peer to send: its stream follows our ZRPOS.
		ITerminalZmodem? zmodem = stream.Zmodem;
		Assert.NotNull(zmodem);
		Task<bool> accepted = zmodem.AcceptAsync(Ct);
		await Eventually.TrueAsync(() => SentHeaders(channel).Any(header => header.Type == ZmodemCode.ZRPOS), "the accept tells the peer to send from zero");

		channel.Emit(PeerData(file));
		channel.Emit(PeerHex(ZmodemCode.ZEOF, file.Length));
		channel.Emit(PeerHex(ZmodemCode.ZFIN));
		channel.Emit("OO");

		Assert.True(await accepted);
		await Eventually.TrueAsync(() => stream.Zmodem?.Phase == ZmodemPhase.Done, "the session ends");
		(string name, byte[] bytes) = Assert.Single(_localFiles.Saved);
		Assert.Equal("log.bin", name);
		Assert.Equal(file, bytes);

		// The notice is a local terminal line, and the channel is a terminal again.
		await Eventually.TrueAsync(() => sink.Text.Contains("zmodem:", StringComparison.Ordinal), "the completion notice lands in the terminal");
		await stream.SendTextAsync("ls", Ct);
		await Eventually.TrueAsync(() => channel.InputText.Contains("ls", StringComparison.Ordinal), "typing reaches the channel again");
	}

	[Fact]
	public async Task Download_DeclinedSave_SkipsTheFile()
	{
		(TerminalStream Stream, FakeTerminalChannel Channel, RecordingSink Sink) harness = Create();

		harness.Channel.Emit(PeerHex(ZmodemCode.ZRQINIT));
		await Eventually.TrueAsync(() => SentHeaders(harness.Channel).Any(header => header.Type == ZmodemCode.ZRINIT), "the invitation is answered");
		harness.Channel.Emit(PeerFile(new ZmodemFileInfo("gone.txt", 3, null, null)));
		await Eventually.TrueAsync(() => harness.Stream.Zmodem?.Phase == ZmodemPhase.Offering, "the offer reaches the view");

		_localFiles.DeclineNext = true;
		ITerminalZmodem? zmodem = harness.Stream.Zmodem;
		Assert.NotNull(zmodem);
		Assert.False(await zmodem.AcceptAsync(Ct));
		Assert.Empty(_localFiles.Saved);
		await Eventually.TrueAsync(() => SentHeaders(harness.Channel).Any(header => header.Type == ZmodemCode.ZSKIP), "the declined file is skipped");
	}

	[Fact]
	public async Task Upload_ThroughThePump_SendsThePickedFile()
	{
		byte[] file = "upload me"u8.ToArray();
		_localFiles.Picked.Add(file);
		_localFiles.PickedNames.Add("send.txt");
		(TerminalStream Stream, FakeTerminalChannel Channel, RecordingSink Sink) harness = Create();

		// rz on the server: its ZRINIT is the invitation to send.
		harness.Channel.Emit("$ rz\r\n");
		harness.Channel.Emit(PeerHex(ZmodemCode.ZRINIT, flags: ZmodemCode.CANFDX | ZmodemCode.CANOVIO | ZmodemCode.CANFC32));
		await Eventually.TrueAsync(() => harness.Stream.Zmodem?.Phase == ZmodemPhase.ChoosingFiles, "the bar asks for files");

		ITerminalZmodem? zmodem = harness.Stream.Zmodem;
		Assert.NotNull(zmodem);
		zmodem.SendFiles(await _localFiles.PickFilesAsync(cancellationToken: Ct));
		await Eventually.TrueAsync(() => SentHeaders(harness.Channel).Any(header => header.Type == ZmodemCode.ZFILE), "the ZFILE goes out");

		// rz answers with the go-ahead at zero; the bytes then flow until our ZEOF.
		harness.Channel.Emit(PeerHex(ZmodemCode.ZRPOS, 0));
		await Eventually.TrueAsync(() => SentPackets(harness.Channel).Any(packet => packet.Data.SequenceEqual(file)), "the picked bytes are sent");
		await Eventually.TrueAsync(() => SentHeaders(harness.Channel).Any(header => header.Type == ZmodemCode.ZEOF), "the file ends on the wire");

		harness.Channel.Emit(PeerHex(ZmodemCode.ZFIN));
		await Eventually.TrueAsync(() => harness.Stream.Zmodem?.Phase == ZmodemPhase.Done, "the session ends");
	}

	[Fact]
	public async Task Cancel_PutsTheCancelSequenceOnTheChannel_AndHandsItBack()
	{
		(TerminalStream Stream, FakeTerminalChannel Channel, RecordingSink Sink) harness = Create();

		harness.Channel.Emit(PeerHex(ZmodemCode.ZRQINIT));
		harness.Channel.Emit(PeerFile(new ZmodemFileInfo("stopped.txt", 10, null, null)));
		await Eventually.TrueAsync(() => harness.Stream.Zmodem?.Phase == ZmodemPhase.Offering, "the offer reaches the view");

		harness.Stream.Zmodem?.Cancel();
		await Eventually.TrueAsync(() => harness.Stream.Zmodem?.Phase == ZmodemPhase.Done, "the session ends");
		await Eventually.TrueAsync(
			() => harness.Channel.InputText.Contains(new string('\u0018', 5), StringComparison.Ordinal),
			"the five-CAN sequence is on the channel");

		await harness.Stream.SendTextAsync("echo done", Ct);
		await Eventually.TrueAsync(() => harness.Channel.InputText.Contains("echo done", StringComparison.Ordinal), "typing reaches the channel again");
	}

	public async ValueTask DisposeAsync()
	{
		foreach (TerminalStream stream in _streams)
		{
			await stream.DisposeAsync();
		}
	}

	private (TerminalStream Stream, FakeTerminalChannel Channel, RecordingSink Sink) Create()
	{
		TerminalStream stream = new(64 * 1024, NullLogger.Instance);
		_streams.Add(stream);
		stream.EnableZmodem(new TerminalZmodem(stream, _localFiles, _time, NullLogger.Instance));
		FakeTerminalChannel channel = new();
		RecordingSink sink = new();
		_ = stream.Attach(sink);
		_ = stream.Bind(channel, Size80X24, generation: 1);
		return (stream, channel, sink);
	}

	private static List<ZmodemHeader> SentHeaders(FakeTerminalChannel channel)
	{
		ZmodemDecoder decoder = Decode(channel);
		return [.. decoder.Events.Where(received => received.Kind == ZmodemReceivedKind.Header).Select(received => received.Header)];
	}

	private static List<ZmodemDataPacket> SentPackets(FakeTerminalChannel channel)
	{
		ZmodemDecoder decoder = Decode(channel);
		return [.. decoder.Events.Where(received => received.Kind == ZmodemReceivedKind.Packet).Select(received => received.Packet!)];
	}

	private static ZmodemDecoder Decode(FakeTerminalChannel channel)
	{
		ZmodemDecoder decoder = new();
		decoder.Feed(channel.InputBytes);
		return decoder;
	}

	/// <summary>The octal mode sz sends, written the way the test reads it.</summary>
	private static long Octal(long digits) => Convert.ToInt64(digits.ToString(System.Globalization.CultureInfo.InvariantCulture), 8);

	/// <summary>A hex header the way the peer sends it.</summary>
	private static byte[] PeerHex(byte type, long position = 0, byte flags = 0)
	{
		ZmodemHeader header = type == ZmodemCode.ZRINIT
			? new ZmodemHeader(type, 0, 0, 0, flags)
			: ZmodemHeader.OfPosition(type, position);
		ZmodemEncoder encoder = new();
		encoder.Header(header, ZmodemHeaderEncoding.Hex);
		return encoder.TakeOutput();
	}

	private static byte[] PeerFile(ZmodemFileInfo info)
	{
		ZmodemEncoder encoder = new();
		encoder.Header(new ZmodemHeader(ZmodemCode.ZFILE, ZmodemCode.ZCBIN, 0, 0, 0, ZmodemHeaderEncoding.Binary32), ZmodemHeaderEncoding.Binary32);
		encoder.Subpacket(info.ToSubpacket(), ZmodemCode.ZCRCW, crc32: true);
		return encoder.TakeOutput();
	}

	private static byte[] PeerData(byte[] file)
	{
		ZmodemEncoder encoder = new();
		encoder.Header(ZmodemHeader.OfPosition(ZmodemCode.ZDATA, 0, ZmodemHeaderEncoding.Binary32), ZmodemHeaderEncoding.Binary32);
		for (int offset = 0; offset < file.Length; offset += 1024)
		{
			int length = Math.Min(1024, file.Length - offset);
			encoder.Subpacket(file.AsSpan(offset, length), offset + length >= file.Length ? ZmodemCode.ZCRCE : ZmodemCode.ZCRCG, crc32: true);
		}

		return encoder.TakeOutput();
	}
}
