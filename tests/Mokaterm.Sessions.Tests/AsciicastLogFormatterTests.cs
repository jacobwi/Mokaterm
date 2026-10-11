using System.Buffers;
using System.Text;
using System.Text.Json;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Sessions.Terminal;

namespace Mokaterm.Sessions.Tests;

public sealed class AsciicastLogFormatterTests
{
	private static readonly TerminalSize Size = new(120, 32);
	private static readonly DateTimeOffset StartedAt = new(2026, 9, 26, 14, 25, 30, TimeSpan.FromHours(2));

	[Fact]
	public void Header_IsOneLineOfVersionTwoJson_WithTheSizeTheTimeAndWhatIsNotRecorded()
	{
		string header = Encoding.UTF8.GetString(new AsciicastLogFormatter().Header("abc@10.0.0.5:22", StartedAt, Size));

		Assert.EndsWith("\n", header, StringComparison.Ordinal);
		Assert.Single(header.Split('\n', StringSplitOptions.RemoveEmptyEntries));
		using JsonDocument document = JsonDocument.Parse(header);
		JsonElement root = document.RootElement;
		Assert.Equal(2, root.GetProperty("version").GetInt32());
		Assert.Equal(120, root.GetProperty("width").GetInt32());
		Assert.Equal(32, root.GetProperty("height").GetInt32());
		Assert.Equal(StartedAt.ToUnixTimeSeconds(), root.GetProperty("timestamp").GetInt64());
		Assert.Equal("abc@10.0.0.5:22", root.GetProperty("title").GetString());
		Assert.Contains("Nothing typed is recorded.", root.GetProperty("mokaterm").GetString(), StringComparison.Ordinal);
	}

	[Fact]
	public void Append_WritesAnOutputEvent_WithEveryCharacterJsonNeedsEscaped()
	{
		AsciicastLogFormatter formatter = Opened();
		const string text = "\u001b[31mred\u001b[0m \"quoted\" back\\slash\ttab\r\n\u00e9t\u00e9 \u65e5\u672c \ud83d\ude00 <tag>";

		(bool whole, string line) = Append(formatter, Encoding.UTF8.GetBytes(text), TimeSpan.FromSeconds(1.5));

		Assert.True(whole);
		Assert.EndsWith("\n", line, StringComparison.Ordinal);
		Assert.Contains("\\u001B[31m", line, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("\\\"quoted\\\"", line, StringComparison.Ordinal);
		Assert.Contains("\\r\\n", line, StringComparison.Ordinal);

		// Text outside ASCII is written as it is, which keeps a recording of a non-English session readable.
		Assert.Contains("\u00e9t\u00e9 \u65e5\u672c", line, StringComparison.Ordinal);
		(decimal time, string code, string data) = ReadEvent(line);
		Assert.Equal(1.5m, time);
		Assert.Equal("o", code);
		Assert.Equal(text, data);
	}

	[Fact]
	public void Append_KeepsACharacterSplitAcrossTwoChunksWhole()
	{
		AsciicastLogFormatter formatter = Opened();
		byte[] bytes = Encoding.UTF8.GetBytes("caf\u00e9");

		(_, string first) = Append(formatter, bytes.AsSpan(0, 4).ToArray(), TimeSpan.Zero);
		(_, string second) = Append(formatter, bytes.AsSpan(4).ToArray(), TimeSpan.FromMilliseconds(5));

		Assert.Equal("caf", ReadEvent(first).Data);
		Assert.Equal("\u00e9", ReadEvent(second).Data);
	}

	[Fact]
	public void Append_TurnsBytesThatAreNotUtf8IntoReplacementCharacters()
	{
		(_, string line) = Append(Opened(), [(byte)'a', 0xff, (byte)'b'], TimeSpan.Zero);

		Assert.Equal("a\ufffdb", ReadEvent(line).Data);
	}

	[Theory]
	[InlineData(0, 0)]
	[InlineData(250, 0.25)]
	[InlineData(61_234.5678, 61.234568)]
	public void Seconds_HasSixDecimalsAtMost(double milliseconds, double expected) =>
		Assert.Equal((decimal)expected, AsciicastLogFormatter.Seconds(TimeSpan.FromMilliseconds(milliseconds)));

	[Fact]
	public void Seconds_NeverGoesBelowZero() => Assert.Equal(0m, AsciicastLogFormatter.Seconds(TimeSpan.FromSeconds(-1)));

	[Fact]
	public void Append_AfterAResize_WritesTheResizeFirst_AndOnlyOnce()
	{
		AsciicastLogFormatter formatter = Opened();

		(_, string resized) = Append(formatter, "x"u8.ToArray(), TimeSpan.FromSeconds(2), new TerminalSize(100, 40));
		(_, string same) = Append(formatter, "y"u8.ToArray(), TimeSpan.FromSeconds(3), new TerminalSize(100, 40));

		string[] lines = resized.Split('\n', StringSplitOptions.RemoveEmptyEntries);
		Assert.Equal(2, lines.Length);
		Assert.Equal((2m, "r", "100x40"), ReadEvent(lines[0]));
		Assert.Equal((2m, "o", "x"), ReadEvent(lines[1]));
		Assert.Single(same.Split('\n', StringSplitOptions.RemoveEmptyEntries));
	}

	[Fact]
	public void Append_PastTheRoomLeft_WritesAsMuchAsFits_AsAWholeLine()
	{
		AsciicastLogFormatter formatter = Opened();
		string text = string.Concat(Enumerable.Repeat("\u001b[1mbold\u001b[0m \ud83d\ude00 ", 50));

		(bool whole, string line) = Append(formatter, Encoding.UTF8.GetBytes(text), TimeSpan.FromSeconds(4), room: 300);

		Assert.False(whole);
		Assert.True(Encoding.UTF8.GetByteCount(line) <= 300);
		(_, string code, string data) = ReadEvent(line);
		Assert.Equal("o", code);
		Assert.NotEmpty(data);
		Assert.StartsWith(data, text, StringComparison.Ordinal);
	}

	[Fact]
	public void Append_WithNoRoomAtAll_WritesNothing()
	{
		(bool whole, string line) = Append(Opened(), "anything"u8.ToArray(), TimeSpan.Zero, room: 0);

		Assert.False(whole);
		Assert.Empty(line);
	}

	[Fact]
	public void LimitNotice_IsAnOutputEventThatSaysTheRestIsNotRecorded()
	{
		string line = Encoding.UTF8.GetString(Opened().LimitNotice(16, TimeSpan.FromSeconds(9)));

		(decimal time, string code, string data) = ReadEvent(line);
		Assert.Equal(9m, time);
		Assert.Equal("o", code);
		Assert.Contains("the rest of this session is not recorded", data, StringComparison.Ordinal);
	}

	[Fact]
	public void TextFormat_PastTheRoomLeft_CutsTheChunkAtTheCap()
	{
		ArrayBufferWriter<byte> output = new();

		bool whole = new TextLogFormatter(filter: null).Append("0123456789"u8, TimeSpan.Zero, Size, room: 4, output);

		Assert.False(whole);
		Assert.Equal("0123", Encoding.UTF8.GetString(output.WrittenSpan));
	}

	private static AsciicastLogFormatter Opened()
	{
		AsciicastLogFormatter formatter = new();
		_ = formatter.Header("abc@10.0.0.5:22", StartedAt, Size);
		return formatter;
	}

	private static (bool Whole, string Text) Append(
		AsciicastLogFormatter formatter,
		byte[] data,
		TimeSpan elapsed,
		TerminalSize? size = null,
		long room = long.MaxValue)
	{
		ArrayBufferWriter<byte> output = new();
		bool whole = formatter.Append(data, elapsed, size ?? Size, room, output);
		return (whole, Encoding.UTF8.GetString(output.WrittenSpan));
	}

	private static (decimal Time, string Code, string Data) ReadEvent(string line)
	{
		using JsonDocument document = JsonDocument.Parse(line);
		JsonElement root = document.RootElement;
		Assert.Equal(3, root.GetArrayLength());
		return (root[0].GetDecimal(), root[1].GetString()!, root[2].GetString()!);
	}
}
