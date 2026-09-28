using System.Buffers;
using System.Text;
using Mokaterm.Modules.Mqtt.Messages;

namespace Mokaterm.Modules.Mqtt.Tests;

public sealed class MqttPayloadFormatterTests
{
	[Fact]
	public void Text_DecodesUtf8()
	{
		Assert.Equal("hallå", MqttPayloadFormatter.Text(Encoding.UTF8.GetBytes("hallå")));
		Assert.Equal("", MqttPayloadFormatter.Text([]));
	}

	[Fact]
	public void Text_ReplacesBytesThatAreNotUtf8RatherThanThrowing()
	{
		string text = MqttPayloadFormatter.Text([0xff, 0xfe]);

		Assert.NotEmpty(text);
		Assert.Contains("�", text, StringComparison.Ordinal);
	}

	[Fact]
	public void Text_CutsAtTheLimitAndSaysSo()
	{
		byte[] payload = Encoding.ASCII.GetBytes(new string('a', MqttPayloadFormatter.MaxTextCharacters + 100));

		string text = MqttPayloadFormatter.Text(payload);

		Assert.Contains("cut here", text, StringComparison.Ordinal);
		Assert.True(text.Length < payload.Length);
	}

	[Fact]
	public void Json_IndentsAnObject()
	{
		string? json = MqttPayloadFormatter.Json(Encoding.UTF8.GetBytes("""{"a":1,"b":[2,3]}"""));

		Assert.NotNull(json);
		Assert.Contains("\n", json, StringComparison.Ordinal);
		Assert.Contains("\"a\": 1", json, StringComparison.Ordinal);
	}

	[Fact]
	public void Json_IndentsAnArray() =>
		Assert.NotNull(MqttPayloadFormatter.Json(Encoding.UTF8.GetBytes("[1,2,3]")));

	[Theory]
	[InlineData("")]
	[InlineData("plain text")]
	[InlineData("12")]
	[InlineData("true")]
	[InlineData("\"a string\"")]
	[InlineData("{not json")]
	[InlineData("{\"a\":")]
	public void Json_IsNullForAnythingThatIsNotAnObjectOrAnArray(string payload) =>
		Assert.Null(MqttPayloadFormatter.Json(Encoding.UTF8.GetBytes(payload)));

	[Fact]
	public void Json_IsNullForNestingDeeperThanTheReaderTakes()
	{
		// JsonDocument refuses past its depth limit, which must come back as "not JSON" and not as an exception.
		string deep = new string('[', 200) + new string(']', 200);

		Assert.Null(MqttPayloadFormatter.Json(Encoding.UTF8.GetBytes(deep)));
	}

	[Fact]
	public void Hex_WritesOffsetsAndTheAsciiBeside()
	{
		string dump = MqttPayloadFormatter.Hex(Encoding.ASCII.GetBytes("AB"));

		Assert.StartsWith("00000000  41 42", dump, StringComparison.Ordinal);
		Assert.Contains("AB", dump, StringComparison.Ordinal);
	}

	[Fact]
	public void Hex_ShowsADotForEveryByteThatIsNotPrintable()
	{
		string dump = MqttPayloadFormatter.Hex([0x00, 0x7f, 0xff]);

		Assert.Contains("...", dump, StringComparison.Ordinal);
	}

	[Fact]
	public void Hex_StopsAtTheLimitAndSaysSo()
	{
		string dump = MqttPayloadFormatter.Hex(new byte[MqttPayloadFormatter.MaxHexBytes + 16]);

		Assert.Contains("cut here", dump, StringComparison.Ordinal);
	}

	[Fact]
	public void Hex_IsEmptyForAnEmptyPayload() => Assert.Equal("", MqttPayloadFormatter.Hex([]));

	[Fact]
	public void Preview_SaysSoForAnEmptyPayload() =>
		Assert.Equal("(empty)", MqttPayloadFormatter.Preview([], 0, 40));

	[Fact]
	public void Preview_CollapsesWhitespaceIntoOneLine() =>
		Assert.Equal("a b", MqttPayloadFormatter.Preview(Encoding.UTF8.GetBytes(" a \r\n\t b "), 9, 40));

	[Fact]
	public void Preview_CutsWithAnEllipsis()
	{
		string preview = MqttPayloadFormatter.Preview(Encoding.ASCII.GetBytes(new string('a', 100)), 100, 10);

		Assert.Equal(11, preview.Length);
		Assert.EndsWith("…", preview, StringComparison.Ordinal);
	}

	[Fact]
	public void Preview_ShowsTheSizeForABinaryPayload() =>
		Assert.Equal("4 B", MqttPayloadFormatter.Preview([0x01, 0x02, 0x03, 0x04], 4, 40));

	[Fact]
	public void LooksLikeText_AcceptsTabsNewlinesAndReturns()
	{
		Assert.True(MqttPayloadFormatter.LooksLikeText(Encoding.UTF8.GetBytes("a\tb\r\nc")));
		Assert.True(MqttPayloadFormatter.LooksLikeText([]));
		Assert.False(MqttPayloadFormatter.LooksLikeText([0x01]));

		// Bytes above 0x7f are UTF-8 continuation bytes, which are text.
		Assert.True(MqttPayloadFormatter.LooksLikeText(Encoding.UTF8.GetBytes("hallå")));
	}

	[Theory]
	[InlineData(0, "0 B")]
	[InlineData(512, "512 B")]
	[InlineData(1024, "1 KB")]
	[InlineData(1536, "1.5 KB")]
	public void Size_ReadsAsAPersonWouldWriteIt(int bytes, string expected) =>
		Assert.Equal(expected, MqttPayloadFormatter.Size(bytes));

	[Fact]
	public void Copy_TakesTheWholePayloadWhenItFits()
	{
		ReadOnlySequence<byte> payload = new(Encoding.ASCII.GetBytes("hello"));

		ReadOnlyMemory<byte> copy = MqttPayloadFormatter.Copy(payload, 1024, out int full);

		Assert.Equal(5, full);
		Assert.Equal("hello", Encoding.ASCII.GetString(copy.Span));
	}

	[Fact]
	public void Copy_CutsToTheCapAndReportsTheRealLength()
	{
		ReadOnlySequence<byte> payload = new(Encoding.ASCII.GetBytes("hello"));

		ReadOnlyMemory<byte> copy = MqttPayloadFormatter.Copy(payload, 2, out int full);

		Assert.Equal(5, full);
		Assert.Equal("he", Encoding.ASCII.GetString(copy.Span));
	}

	[Fact]
	public void Copy_ReadsAcrossTheSegmentsOfASequence()
	{
		// MQTTnet hands over a ReadOnlySequence, which a large payload can arrive in several segments of.
		ReadOnlySequence<byte> payload = Split("hello "u8.ToArray(), "world"u8.ToArray());

		ReadOnlyMemory<byte> copy = MqttPayloadFormatter.Copy(payload, 1024, out int full);

		Assert.Equal(11, full);
		Assert.Equal("hello world", Encoding.ASCII.GetString(copy.Span));
	}

	[Fact]
	public void Copy_WithACapOfZero_KeepsNothingAndStillReportsTheLength()
	{
		ReadOnlyMemory<byte> copy = MqttPayloadFormatter.Copy(new ReadOnlySequence<byte>(new byte[9]), 0, out int full);

		Assert.Equal(9, full);
		Assert.True(copy.IsEmpty);
	}

	private static ReadOnlySequence<byte> Split(byte[] first, byte[] second)
	{
		Segment head = new(first);
		Segment tail = head.Append(second);
		return new ReadOnlySequence<byte>(head, 0, tail, second.Length);
	}

	private sealed class Segment : ReadOnlySequenceSegment<byte>
	{
		public Segment(byte[] memory) => Memory = memory;

		public Segment Append(byte[] memory)
		{
			Segment next = new(memory) { RunningIndex = RunningIndex + Memory.Length };
			Next = next;
			return next;
		}
	}
}
