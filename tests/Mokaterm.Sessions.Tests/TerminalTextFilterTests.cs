using System.Text;
using Mokaterm.Sessions.Terminal;

namespace Mokaterm.Sessions.Tests;

public sealed class TerminalTextFilterTests
{
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

	private static string Filter(byte[] input)
	{
		TerminalTextFilter filter = new();
		byte[] output = new byte[input.Length];
		return Encoding.UTF8.GetString(output, 0, filter.Filter(input, output));
	}
}
