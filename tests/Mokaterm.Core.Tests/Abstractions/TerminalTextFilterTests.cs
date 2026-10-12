using System.Text;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.Core.Tests.Abstractions;

public sealed class TerminalTextFilterTests
{
	private const char Esc = (char)0x1B;
	private const char Bell = (char)0x07;

	[Theory]
	[InlineData("plain text", "plain text")]
	[InlineData("[32mgreen[0m", "green")]
	[InlineData("[1;31;40mmany parameters[m", "many parameters")]
	[InlineData("before]0;a window titleafter", "beforeafter")]
	[InlineData("before]0;a window title\\after", "beforeafter")]
	[InlineData("keeps\ttabs\r\nand lines\n", "keeps\ttabs\r\nand lines\n")]
	[InlineData("bellandbackspace", "bellandbackspace")]
	[InlineData("(Bcharset=keypad", "charsetkeypad")]
	[InlineData("[2J[H[?25lcleared[?25h", "cleared")]
	public void Filter_DropsSequencesAndControlBytes(string input, string expected) =>
		Assert.Equal(expected, Filter(Encoding.UTF8.GetBytes(input)));

	[Fact]
	public void Filter_KeepsTextOverAscii()
	{
		// The C1 range a terminal reads as controls is every UTF-8 continuation byte as well, so bytes over 0x7f pass.
		const string Text = "Grüße, 日本語, emoji \U0001f600";

		Assert.Equal(Text, Filter(Encoding.UTF8.GetBytes(Text)));
	}

	[Fact]
	public void Filter_ASequenceSplitAcrossChunks_IsStillDropped()
	{
		TerminalTextFilter filter = new();
		byte[] whole = Encoding.UTF8.GetBytes("a[38;5;214mb]0;titlec");
		StringBuilder text = new();
		for (int i = 0; i < whole.Length; i++)
		{
			byte[] output = new byte[1];
			text.Append(Encoding.UTF8.GetString(output, 0, filter.Filter(whole.AsSpan(i, 1), output)));
		}

		Assert.Equal("abc", text.ToString());
	}

	[Fact]
	public void Filter_AnEscapeInsideASequence_StartsANewOne()
	{
		// A truncated sequence is what a dropped connection leaves behind; the text after it must not be swallowed.
		Assert.Equal("kept", Filter(Encoding.UTF8.GetBytes("[38;5[0mkept")));
	}

	[Fact]
	public void Plain_DropsWhatFilterDrops_AndEndsEveryLineWithALineFeed()
	{
		string input = $"{Esc}[31mred{Esc}[0m plain\ttab\r\nnext{Bell} title{Esc}]0;window{Bell} gone{Esc}(B";

		Assert.Equal("red plain\ttab\nnext title gone", TerminalTextFilter.Plain(input));
	}

	[Fact]
	public void Plain_ALoneCarriageReturn_StartsANewLine()
	{
		// A progress line redrawn with CR: in a static box each step reads under the last, not glued to it.
		Assert.Equal("10%\n50%\ndone\n", TerminalTextFilter.Plain("10%\r50%\rdone\r\n"));
	}

	[Fact]
	public void Plain_DropsTheC1Controls_ButKeepsEveryOtherCharacter()
	{
		string text = "Gr" + (char)0xFC + (char)0xDF + "e, " + (char)0x65E5 + (char)0x672C + ", " + char.ConvertFromUtf32(0x1F600);

		Assert.Equal(text, TerminalTextFilter.Plain((char)0x9B + text + (char)0x85));
	}

	[Fact]
	public void Plain_TextWithNothingToDrop_IsReturnedAsItIs()
	{
		const string Text = "nothing\tto\ndrop";

		Assert.Same(Text, TerminalTextFilter.Plain(Text));
	}

	[Fact]
	public void Plain_EveryStringSequence_IsDropped_AndARepeatedEscapeStartsOver()
	{
		Assert.Equal("ab", TerminalTextFilter.Plain($"a{Esc}Pdevice control{Esc}\\b{Esc}_application"));
		Assert.Equal("kept", TerminalTextFilter.Plain($"{Esc}{Esc}[1mkept"));
	}

	private static string Filter(byte[] input)
	{
		TerminalTextFilter filter = new();
		byte[] output = new byte[input.Length];
		return Encoding.UTF8.GetString(output, 0, filter.Filter(input, output));
	}
}
