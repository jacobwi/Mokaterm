namespace Mokaterm.DevHost.Demo.Terminal;

internal enum TerminalKeyKind
{
	/// <summary>Printable characters, in <see cref="TerminalKey.Text"/>.</summary>
	Text,
	Enter,
	Backspace,
	Tab,
	Interrupt,
	EndOfFile,
	ClearScreen,
	HistoryPrevious,
	HistoryNext,
}

/// <summary>One key the line editor acts on.</summary>
internal readonly record struct TerminalKey(TerminalKeyKind Kind, string Text = "");
