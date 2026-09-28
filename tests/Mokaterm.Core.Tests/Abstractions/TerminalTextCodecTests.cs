using System.Buffers;
using System.Text;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.Core.Tests.Abstractions;

/// <summary>
/// The codec every terminal protocol transcodes with. The cases come from the telnet and serial suites it replaced,
/// so both a socket read and a driver read that cut a character in half are covered here.
/// </summary>
public sealed class TerminalTextCodecTests
{
	[Fact]
	public void Utf8_PassesEveryByteThrough()
	{
		TerminalTextCodec codec = new(Encoding.UTF8);

		Assert.True(codec.IsPassthrough);
		Assert.Equal("héllo"u8.ToArray(), ToTerminal(codec, "héllo"u8.ToArray()));
		Assert.Equal("héllo"u8.ToArray(), FromTerminal(codec, "héllo"u8.ToArray()));
	}

	[Fact]
	public void CodePage_ToTerminal_ArrivesAsUtf8()
	{
		TerminalTextCodec codec = new(TerminalEncodings.Resolve("windows-1252"));

		byte[] terminal = ToTerminal(codec, [0xE9, 0x21]);

		Assert.False(codec.IsPassthrough);
		Assert.Equal("é!", Encoding.UTF8.GetString(terminal));
	}

	[Fact]
	public void CodePage_FromTerminal_LeavesTheWireInItsOwnCharacterSet()
	{
		TerminalTextCodec codec = new(TerminalEncodings.Resolve("windows-1252"));

		byte[] wire = FromTerminal(codec, Encoding.UTF8.GetBytes("é!"));

		Assert.Equal([0xE9, 0x21], wire);
	}

	[Fact]
	public void CodePage_Utf8CharacterSplitAcrossCalls_StillArrives()
	{
		TerminalTextCodec codec = new(TerminalEncodings.Resolve("windows-1252"));
		byte[] utf8 = Encoding.UTF8.GetBytes("é");

		byte[] first = FromTerminal(codec, utf8[..1]);
		byte[] second = FromTerminal(codec, utf8[1..]);

		Assert.Empty(first);
		Assert.Equal([0xE9], second);
	}

	[Fact]
	public void CodePage_CharacterSplitAcrossTwoReads_StillArrivesWhole()
	{
		TerminalTextCodec codec = new(TerminalEncodings.Resolve("utf-32"));
		byte[] full = Encoding.UTF32.GetBytes("Ü");

		// The first half holds no whole character, so nothing can be shown yet.
		Assert.Empty(ToTerminal(codec, full[..2]));
		Assert.Equal("Ü"u8.ToArray(), ToTerminal(codec, full[2..]));
	}

	[Fact]
	public void CodePage_UnmappableCharacter_BecomesAQuestionMarkInsteadOfThrowing()
	{
		TerminalTextCodec codec = new(TerminalEncodings.Resolve("us-ascii"));

		byte[] wire = FromTerminal(codec, Encoding.UTF8.GetBytes("é"));

		Assert.Equal([(byte)'?'], wire);
	}

	[Fact]
	public void CodePage_AByteNoCharacterMapsTo_IsReplacedRatherThanThrown()
	{
		TerminalTextCodec codec = new(TerminalEncodings.Resolve("us-ascii"));

		// A line with noise on it must not end a session, so the character substitutes.
		Assert.NotEmpty(ToTerminal(codec, [0xFF]));
	}

	[Fact]
	public void CodePage_EscapeSequences_Survive()
	{
		TerminalTextCodec codec = new(TerminalEncodings.Resolve("ibm437"));

		Assert.Equal("\u001b[2J"u8.ToArray(), ToTerminal(codec, "\u001b[2J"u8.ToArray()));
	}

	private static byte[] ToTerminal(TerminalTextCodec codec, byte[] input)
	{
		ArrayBufferWriter<byte> writer = new(64);
		codec.ToTerminal(input, writer);
		return writer.WrittenSpan.ToArray();
	}

	private static byte[] FromTerminal(TerminalTextCodec codec, byte[] input)
	{
		ArrayBufferWriter<byte> writer = new(64);
		codec.FromTerminal(input, writer);
		return writer.WrittenSpan.ToArray();
	}
}
