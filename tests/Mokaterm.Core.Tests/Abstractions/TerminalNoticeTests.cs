using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.Core.Tests.Abstractions;

/// <summary>The app's own lines in a terminal: on a line of their own, dimmed, marked, and never carrying a control character.</summary>
public sealed class TerminalNoticeTests
{
	private const char Escape = (char)0x1B;

	[Fact]
	public void Format_PutsTheNoticeOnALineOfItsOwn_DimmedAndMarked() =>
		Assert.Equal($"\r\n{Escape}[2m[mokaterm] connection closed{Escape}[0m\r\n", TerminalNotice.Format("connection closed"));

	[Fact]
	public void Format_TurnsControlCharactersIntoSpaces() =>
		Assert.Equal($"\r\n{Escape}[2m[mokaterm] lost:   ]0;x {Escape}[0m\r\n", TerminalNotice.Format($"lost: \n{Escape}]0;x\a"));
}
