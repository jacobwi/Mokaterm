using Mokaterm.Modules.Telnet.Protocol;

namespace Mokaterm.Modules.Telnet.Tests;

public sealed class TelnetInputParserTests
{
	[Fact]
	public void Feed_PlainData_ComesOutUnchanged()
	{
		(byte[] data, List<TelnetMessage> messages) = Feed(new TelnetInputParser(), "hi"u8.ToArray());

		Assert.Equal("hi"u8.ToArray(), data);
		Assert.Empty(messages);
	}

	[Fact]
	public void Feed_DoubledIac_IsOneDataByte()
	{
		(byte[] data, List<TelnetMessage> messages) = Feed(
			new TelnetInputParser(),
			[(byte)'a', TelnetCommand.Iac, TelnetCommand.Iac, (byte)'b']);

		Assert.Equal([(byte)'a', 255, (byte)'b'], data);
		Assert.Empty(messages);
	}

	[Fact]
	public void Feed_Negotiation_IsTakenOutOfTheData()
	{
		(byte[] data, List<TelnetMessage> messages) = Feed(
			new TelnetInputParser(),
			[(byte)'a', TelnetCommand.Iac, TelnetCommand.Will, TelnetOption.Echo, (byte)'b']);

		Assert.Equal("ab"u8.ToArray(), data);
		Assert.Equal(TelnetMessage.Negotiation(TelnetCommand.Will, TelnetOption.Echo), Assert.Single(messages));
	}

	[Fact]
	public void Feed_NegotiationSplitAcrossChunks_IsStillOneMessage()
	{
		TelnetInputParser parser = new();

		(byte[] first, List<TelnetMessage> firstMessages) = Feed(parser, [(byte)'x', TelnetCommand.Iac]);
		(byte[] second, List<TelnetMessage> secondMessages) = Feed(parser, [TelnetCommand.Do]);
		(byte[] third, List<TelnetMessage> thirdMessages) = Feed(parser, [TelnetOption.NegotiateAboutWindowSize, (byte)'y']);

		Assert.Equal("x"u8.ToArray(), first);
		Assert.Empty(firstMessages);
		Assert.Empty(second);
		Assert.Empty(secondMessages);
		Assert.Equal("y"u8.ToArray(), third);
		Assert.Equal(
			TelnetMessage.Negotiation(TelnetCommand.Do, TelnetOption.NegotiateAboutWindowSize),
			Assert.Single(thirdMessages));
	}

	[Fact]
	public void Feed_Subnegotiation_UnescapesThePayload()
	{
		(byte[] data, List<TelnetMessage> messages) = Feed(
			new TelnetInputParser(),
			[
				TelnetCommand.Iac, TelnetCommand.Sb, TelnetOption.TerminalType, TelnetOption.Is,
				(byte)'v', TelnetCommand.Iac, TelnetCommand.Iac, (byte)'t',
				TelnetCommand.Iac, TelnetCommand.Se,
			]);

		Assert.Empty(data);
		TelnetMessage message = Assert.Single(messages);
		Assert.True(message.IsSubnegotiation);
		Assert.Equal(TelnetOption.TerminalType, message.Option);
		Assert.Equal([TelnetOption.Is, (byte)'v', 255, (byte)'t'], message.Payload);
	}

	[Fact]
	public void Feed_SubnegotiationSplitAcrossChunks_ArrivesWhole()
	{
		TelnetInputParser parser = new();

		Feed(parser, [TelnetCommand.Iac, TelnetCommand.Sb, TelnetOption.NegotiateAboutWindowSize, 0]);
		Feed(parser, [80, 0]);
		(_, List<TelnetMessage> messages) = Feed(parser, [24, TelnetCommand.Iac, TelnetCommand.Se]);

		TelnetMessage message = Assert.Single(messages);
		Assert.Equal([0, 80, 0, 24], message.Payload);
	}

	[Fact]
	public void Feed_BareCommand_IsReported()
	{
		(byte[] data, List<TelnetMessage> messages) = Feed(
			new TelnetInputParser(),
			[TelnetCommand.Iac, TelnetCommand.Nop, (byte)'z']);

		Assert.Equal("z"u8.ToArray(), data);
		Assert.Equal(TelnetMessage.Bare(TelnetCommand.Nop), Assert.Single(messages));
	}

	[Fact]
	public void Feed_HugeSubnegotiation_IsDroppedInsteadOfBuffered()
	{
		TelnetInputParser parser = new();
		List<byte> input = [TelnetCommand.Iac, TelnetCommand.Sb, TelnetOption.TerminalType];
		input.AddRange(Enumerable.Repeat((byte)'a', TelnetInputParser.MaxSubnegotiationLength + 1));
		input.AddRange([TelnetCommand.Iac, TelnetCommand.Se, (byte)'k']);

		(byte[] data, List<TelnetMessage> messages) = Feed(parser, [.. input]);

		Assert.Equal("k"u8.ToArray(), data);
		Assert.Empty(messages);
	}

	[Fact]
	public void Feed_CommandInsideSubnegotiation_EndsItAndIsReported()
	{
		(_, List<TelnetMessage> messages) = Feed(
			new TelnetInputParser(),
			[
				TelnetCommand.Iac, TelnetCommand.Sb, TelnetOption.TerminalType, TelnetOption.Is, (byte)'a',
				TelnetCommand.Iac, TelnetCommand.Will, TelnetOption.Echo,
			]);

		Assert.Equal(TelnetMessage.Negotiation(TelnetCommand.Will, TelnetOption.Echo), Assert.Single(messages));
	}

	[Fact]
	public void Feed_DataBufferSmallerThanTheInput_Throws()
	{
		TelnetInputParser parser = new();

		Assert.Throws<ArgumentException>(() => parser.Feed(new byte[4], new byte[2], []));
	}

	private static (byte[] Data, List<TelnetMessage> Messages) Feed(TelnetInputParser parser, byte[] input)
	{
		byte[] data = new byte[input.Length];
		List<TelnetMessage> messages = [];
		int length = parser.Feed(input, data, messages);
		return (data[..length], messages);
	}
}
