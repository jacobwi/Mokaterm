using System.Text;
using Mokaterm.UI.Terminal.Playback;

namespace Mokaterm.UI.Tests;

public sealed class TerminalRecordingReaderTests
{
	private const string Header = """{"version": 2, "width": 100, "height": 30, "timestamp": 1790000000, "title": "abc@web01:22"}""";

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public void Cast_ReadsTheGridAndTheEventsInOrder()
	{
		TerminalRecording recording = Cast(
			Header,
			"""[0.1, "o", "hello "]""",
			"""[0.5, "r", "120x40"]""",
			"""[0.75, "o", "world\r\n"]""");

		Assert.True(recording.IsTimed);
		Assert.Equal(100, recording.Columns);
		Assert.Equal(30, recording.Rows);
		Assert.Equal(
			[
				RecordingEvent.Text(Seconds(0.1), "hello "),
				RecordingEvent.Resize(Seconds(0.5), 120, 40),
				RecordingEvent.Text(Seconds(0.75), "world\r\n"),
			],
			recording.Events);
		Assert.Equal(Seconds(0.75), recording.Duration);
		Assert.Equal(0, recording.SkippedLines);
	}

	[Fact]
	public void Cast_MalformedLines_AreCountedAndSkipped_NotFatal()
	{
		TerminalRecording recording = Cast(
			Header,
			"""[0.1, "o", "one"]""",
			"not json at all",
			"""[0.2, "o", "two" """,
			"""["soon", "o", "three"]""",
			"""[0.3, "o", 42]""",
			"""[0.4, "o"]""",
			"""[0.45, "o", "\ud800 half a pair"]""",
			"""{"an": "object"}""",
			"""[0.5, "r", "wide"]""",
			"""[0.6, "i", "typed"]""",
			"""[0.7, "m", "a marker"]""",
			"""[0.8, "o", "four"]""",
			"");

		Assert.Equal(["one", "four"], recording.Events.Select(recorded => recorded.Output));
		Assert.Equal(8, recording.SkippedLines);
	}

	[Fact]
	public void Cast_IdleGaps_AreCutToTheLimit()
	{
		TerminalRecording recording = Cast(
			Header,
			"""[1, "o", "a"]""",
			"""[61, "o", "b"]""",
			"""[61.5, "o", "c"]""",
			"""[3600, "o", "d"]""");

		Assert.Equal(
			[Seconds(1), Seconds(1) + TerminalRecordingReader.IdleLimit, Seconds(1.5) + TerminalRecordingReader.IdleLimit, Seconds(1.5) + (2 * TerminalRecordingReader.IdleLimit)],
			recording.Events.Select(recorded => recorded.Time));
	}

	[Fact]
	public void Cast_AShorterIdleLimitInTheHeader_IsKept()
	{
		TerminalRecording recording = Cast(
			"""{"version": 2, "width": 80, "height": 24, "idle_time_limit": 0.5}""",
			"""[0, "o", "a"]""",
			"""[10, "o", "b"]""");

		Assert.Equal(Seconds(0.5), recording.Events[1].Time);
	}

	[Fact]
	public void Cast_TimesThatGoBackwards_CountAsNoTimeAtAll()
	{
		TerminalRecording recording = Cast(
			Header,
			"""[1, "o", "a"]""",
			"""[0.2, "o", "b"]""",
			"""[-5, "o", "c"]""",
			"""[1.5, "o", "d"]""");

		// The three at one second are also one event now, being closer together than the merge window.
		Assert.Equal(["abc", "d"], recording.Events.Select(recorded => recorded.Output));
		Assert.Equal([Seconds(1), Seconds(1.5)], recording.Events.Select(recorded => recorded.Time));
	}

	[Fact]
	public void Cast_OutputCloserThanTheMergeWindow_BecomesOneEvent()
	{
		TerminalRecording recording = Cast(
			Header,
			"""[1.000, "o", "a"]""",
			"""[1.004, "o", "b"]""",
			"""[1.009, "o", "c"]""",
			"""[1.5, "o", "d"]""");

		Assert.Equal(["abc", "d"], recording.Events.Select(recorded => recorded.Output));
		Assert.Equal(Seconds(1), recording.Events[0].Time);
	}

	[Fact]
	public void Cast_LongOutput_IsSplitIntoEventsAtTheSameTime()
	{
		string text = new('x', (TerminalRecordingReader.MaxEventLength * 2) + 10);

		TerminalRecording recording = Cast(Header, $$"""[2, "o", "{{text}}"]""");

		Assert.Equal(3, recording.Events.Count);
		Assert.All(recording.Events, recorded => Assert.Equal(Seconds(2), recorded.Time));
		Assert.Equal(text, string.Concat(recording.Events.Select(recorded => recorded.Output)));
	}

	[Fact]
	public void Cast_AGridPastTheLimit_IsHeldToIt()
	{
		TerminalRecording recording = Cast(
			"""{"version": 2, "width": 100000, "height": 0}""",
			"""[1, "r", "99999x99999"]""");

		Assert.Equal(TerminalRecordingReader.MaxColumns, recording.Columns);
		Assert.Equal(24, recording.Rows);
		Assert.Equal(RecordingEvent.Resize(Seconds(1), TerminalRecordingReader.MaxColumns, TerminalRecordingReader.MaxRows), recording.Events[0]);
	}

	[Fact]
	public void Cast_WithoutAHeader_PlaysAtTheClassicSize()
	{
		TerminalRecording recording = Cast("""[0.5, "o", "cut at the front"]""");

		Assert.Equal(80, recording.Columns);
		Assert.Equal(24, recording.Rows);
		Assert.Single(recording.Events);
	}

	[Fact]
	public void Cast_VersionThree_AddsUpIntervals_AndReadsTheGridFromTerm()
	{
		TerminalRecording recording = Cast(
			"""{"version": 3, "term": {"cols": 132, "rows": 43}}""",
			"""[0.5, "o", "a"]""",
			"""[0.25, "o", "b"]""",
			"""[0.5, "i", "typed"]""",
			"""[100, "o", "c"]""",
			"""[0.1, "x", "0"]""");

		Assert.Equal(132, recording.Columns);
		Assert.Equal(43, recording.Rows);
		// An input event shows nothing, but its interval still passed.
		Assert.Equal([Seconds(0.5), Seconds(0.75), Seconds(1.25) + TerminalRecordingReader.IdleLimit], recording.Events.Select(recorded => recorded.Time));
	}

	[Theory]
	[InlineData("{\"version\": 1}", "version 1")]
	[InlineData("{\"width\": 80}", "does not start like")]
	[InlineData("hello there", "does not start like")]
	[InlineData("{\"version\": 2", "does not start like")]
	[InlineData("{\"version\": \"2\"}", "does not start like")]
	public void Cast_ThatIsNotOne_IsRefusedWithAReason(string firstLine, string reason)
	{
		TerminalRecordingResult result = TerminalRecordingReader.Parse(Encoding.UTF8.GetBytes(firstLine + "\n[0, \"o\", \"x\"]\n"), "a.cast");

		Assert.Null(result.Recording);
		Assert.Contains(reason, result.Error, StringComparison.Ordinal);
	}

	[Fact]
	public void Cast_WithNothingPlayable_IsRefused()
	{
		string[] lines = [Header, .. Enumerable.Repeat("[\"junk\"]", TerminalRecordingReader.MaxSkippedLines)];

		TerminalRecordingResult result = TerminalRecordingReader.Parse(Encoding.UTF8.GetBytes(string.Join('\n', lines)), "a.cast");

		Assert.Equal("Nothing in this file could be played.", result.Error);
	}

	[Fact]
	public void Cast_WithCrLfLineEndsAndAByteOrderMark_IsRead()
	{
		byte[] content = [.. "\ufeff"u8, .. Encoding.UTF8.GetBytes(Header + "\r\n[0.1, \"o\", \"a\"]\r\n")];

		TerminalRecording? recording = TerminalRecordingReader.Parse(content, "a.CAST").Recording;

		Assert.NotNull(recording);
		Assert.Equal("a", recording.Events[0].Output);
		Assert.Equal(0, recording.SkippedLines);
	}

	[Fact]
	public void IndexAt_CountsTheEventsAtOrBeforeAPosition()
	{
		TerminalRecording recording = Cast(
			Header,
			"""[0, "o", "a"]""",
			"""[0, "o", "b"]""",
			"""[1, "o", "c"]""",
			"""[2, "o", "d"]""");

		Assert.Equal(1, recording.IndexAt(TimeSpan.Zero));
		Assert.Equal(1, recording.IndexAt(Seconds(0.999)));
		Assert.Equal(2, recording.IndexAt(Seconds(1)));
		Assert.Equal(3, recording.IndexAt(Seconds(2)));
		Assert.Equal(3, recording.IndexAt(TimeSpan.FromHours(1)));
		Assert.Equal(0, recording.IndexAt(TimeSpan.FromSeconds(-1)));
	}

	[Fact]
	public void PlainLog_IsShownWholeAtOnce_WithBareLineFeedsMadeLineEnds()
	{
		TerminalRecordingResult result = TerminalRecordingReader.Parse("[mokaterm] session log\r\nline one\nline two\n"u8, "web01.log");

		TerminalRecording recording = Assert.IsType<TerminalRecording>(result.Recording);
		Assert.False(recording.IsTimed);
		Assert.Equal(TimeSpan.Zero, recording.Duration);
		Assert.Equal("[mokaterm] session log\r\nline one\r\nline two\r\n", string.Concat(recording.Events.Select(recorded => recorded.Output)));
		Assert.Equal(recording.Events.Count, recording.IndexAt(TimeSpan.Zero));
	}

	[Fact]
	public void RawLog_KeepsBareLineFeeds_WhichMoveTheCursorDown()
	{
		TerminalRecording? recording = TerminalRecordingReader.Parse("\u001b[2Jtop\ndown\r\n"u8, "web01.log").Recording;

		Assert.NotNull(recording);
		Assert.Equal("\u001b[2Jtop\ndown\r\n", recording.Events[0].Output);
	}

	[Fact]
	public void Log_ThatIsNotUtf8_IsShownWithReplacementCharacters()
	{
		TerminalRecording? recording = TerminalRecordingReader.Parse([(byte)'a', 0xff, (byte)'b'], "x.log").Recording;

		Assert.Equal("a\ufffdb", recording?.Events[0].Output);
	}

	[Fact]
	public async Task ReadAsync_AFilePastTheCap_IsRefusedBeforeItIsRead()
	{
		await using CountingStream stream = new(TerminalRecordingReader.MaxFileBytes + 1);

		TerminalRecordingResult result = await TerminalRecordingReader.ReadAsync(stream, "big.cast", Ct);

		Assert.Null(result.Recording);
		Assert.Contains("the player opens files up to 16 MB", result.Error, StringComparison.Ordinal);
		Assert.Equal(0, stream.BytesRead);
	}

	[Fact]
	public async Task ReadAsync_AStreamThatGrowsPastTheCap_StopsThere()
	{
		await using CountingStream stream = new(TerminalRecordingReader.MaxFileBytes + 1, seekable: false);

		TerminalRecordingResult result = await TerminalRecordingReader.ReadAsync(stream, "big.cast", Ct);

		Assert.Null(result.Recording);
		Assert.NotNull(result.Error);
		Assert.True(stream.BytesRead <= TerminalRecordingReader.MaxFileBytes + (128 * 1024));
	}

	[Fact]
	public async Task ReadAsync_ReadsAndParses()
	{
		await using MemoryStream stream = new(Encoding.UTF8.GetBytes(Header + "\n[0.25, \"o\", \"hi\"]\n"));

		TerminalRecordingResult result = await TerminalRecordingReader.ReadAsync(stream, "a.cast", Ct);

		Assert.Equal("hi", result.Recording?.Events.Single().Output);
	}

	private static TimeSpan Seconds(double seconds) => TimeSpan.FromSeconds(seconds);

	private static TerminalRecording Cast(params string[] lines)
	{
		TerminalRecordingResult result = TerminalRecordingReader.Parse(Encoding.UTF8.GetBytes(string.Join('\n', lines)), "session.cast");
		Assert.Null(result.Error);
		return result.Recording!;
	}

	/// <summary>A stream of spaces of a given length that counts what was read from it.</summary>
	private sealed class CountingStream(long length, bool seekable = true) : Stream
	{
		private long _position;

		public long BytesRead { get; private set; }

		public override bool CanRead => true;

		public override bool CanSeek => seekable;

		public override bool CanWrite => false;

		public override long Length => seekable ? length : throw new NotSupportedException();

		public override long Position
		{
			get => _position;
			set => throw new NotSupportedException();
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			int read = (int)Math.Min(count, length - _position);
			Array.Fill(buffer, (byte)' ', offset, read);
			_position += read;
			BytesRead += read;
			return read;
		}

		public override void Flush()
		{
		}

		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

		public override void SetLength(long value) => throw new NotSupportedException();

		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	}
}
