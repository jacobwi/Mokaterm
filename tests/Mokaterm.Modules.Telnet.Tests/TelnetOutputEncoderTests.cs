using System.Buffers;
using Mokaterm.Modules.Telnet.Protocol;

namespace Mokaterm.Modules.Telnet.Tests;

public sealed class TelnetOutputEncoderTests
{
	[Theory]
	[InlineData(TelnetLineEnding.CrLf, new byte[] { 0x0D, 0x0A })]
	[InlineData(TelnetLineEnding.CrNul, new byte[] { 0x0D, 0x00 })]
	[InlineData(TelnetLineEnding.Lf, new byte[] { 0x0A })]
	public void Write_Enter_SendsTheConfiguredLineEnding(TelnetLineEnding lineEnding, byte[] expected)
	{
		byte[] written = Write(new TelnetOutputEncoder(lineEnding), "a\r"u8.ToArray());

		Assert.Equal([(byte)'a', .. expected], written);
	}

	[Fact]
	public void Write_PastedCarriageReturnLineFeed_IsOneLineEnding()
	{
		byte[] written = Write(new TelnetOutputEncoder(TelnetLineEnding.CrNul), "a\r\nb"u8.ToArray());

		Assert.Equal([(byte)'a', 0x0D, 0x00, (byte)'b'], written);
	}

	[Fact]
	public void Write_CarriageReturnAndLineFeedInSeparateCalls_IsStillOneLineEnding()
	{
		TelnetOutputEncoder encoder = new(TelnetLineEnding.Lf);

		byte[] first = Write(encoder, "a\r"u8.ToArray());
		byte[] second = Write(encoder, "\nb"u8.ToArray());

		Assert.Equal([(byte)'a', 0x0A], first);
		Assert.Equal("b"u8.ToArray(), second);
	}

	[Fact]
	public void Write_LineFeedOnItsOwn_IsLeftAlone()
	{
		byte[] written = Write(new TelnetOutputEncoder(TelnetLineEnding.CrLf), "a\nb"u8.ToArray());

		Assert.Equal("a\nb"u8.ToArray(), written);
	}

	[Fact]
	public void Write_Iac_IsDoubled()
	{
		byte[] written = Write(new TelnetOutputEncoder(TelnetLineEnding.CrLf), [(byte)'a', 255, (byte)'b']);

		Assert.Equal([(byte)'a', 255, 255, (byte)'b'], written);
	}

	[Fact]
	public void Write_NothingToEscape_CopiesTheInput()
	{
		byte[] written = Write(new TelnetOutputEncoder(TelnetLineEnding.CrLf), "plain text"u8.ToArray());

		Assert.Equal("plain text"u8.ToArray(), written);
	}

	private static byte[] Write(TelnetOutputEncoder encoder, byte[] input)
	{
		ArrayBufferWriter<byte> writer = new();
		encoder.Write(input, writer);
		return writer.WrittenSpan.ToArray();
	}
}
