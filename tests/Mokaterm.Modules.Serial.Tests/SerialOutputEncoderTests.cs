using System.Buffers;
using Mokaterm.Modules.Serial.Protocol;

namespace Mokaterm.Modules.Serial.Tests;

public sealed class SerialOutputEncoderTests
{
	[Theory]
	[InlineData(SerialLineEnding.Cr, new byte[] { 0x0D })]
	[InlineData(SerialLineEnding.CrLf, new byte[] { 0x0D, 0x0A })]
	[InlineData(SerialLineEnding.Lf, new byte[] { 0x0A })]
	public void Enter_BecomesTheEndingTheConnectionUses(SerialLineEnding lineEnding, byte[] expected) =>
		Assert.Equal(expected, Encode(lineEnding, [0x0D]));

	[Fact]
	public void PlainCharacters_GoOutUnchanged() =>
		Assert.Equal("ls -l"u8.ToArray(), Encode(SerialLineEnding.Cr, "ls -l"u8.ToArray()));

	[Fact]
	public void EveryByteValueIsData_UnlikeTelnetWhichEscapes255()
	{
		byte[] input = [0xFF, 0xFF, 0x00, 0x80];

		Assert.Equal(input, Encode(SerialLineEnding.Cr, input));
	}

	[Fact]
	public void PastedCrLf_IsOneEnding() =>
		Assert.Equal(new byte[] { 0x0D, 0x0A, (byte)'x' }, Encode(SerialLineEnding.CrLf, [0x0D, 0x0A, (byte)'x']));

	[Fact]
	public void PastedCrLf_WithAnLfEnding_IsOneLineFeed() =>
		Assert.Equal(new byte[] { 0x0A }, Encode(SerialLineEnding.Lf, [0x0D, 0x0A]));

	[Fact]
	public void LineFeedThatArrivesInTheNextWrite_IsStillPartOfTheSameEnter()
	{
		SerialOutputEncoder encoder = new(SerialLineEnding.Cr);

		Assert.Equal(new byte[] { 0x0D }, Write(encoder, [0x0D]));
		Assert.Equal(new byte[] { (byte)'y' }, Write(encoder, [0x0A, (byte)'y']));
	}

	[Fact]
	public void LineFeedOnItsOwn_IsNotAnEnterAndTravelsAsItIs() =>
		Assert.Equal(new byte[] { 0x0A }, Encode(SerialLineEnding.Cr, [0x0A]));

	private static byte[] Encode(SerialLineEnding lineEnding, byte[] input) => Write(new SerialOutputEncoder(lineEnding), input);

	private static byte[] Write(SerialOutputEncoder encoder, byte[] input)
	{
		ArrayBufferWriter<byte> writer = new(16);
		encoder.Write(input, writer);
		return writer.WrittenSpan.ToArray();
	}
}
