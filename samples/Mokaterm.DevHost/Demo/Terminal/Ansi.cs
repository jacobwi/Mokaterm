namespace Mokaterm.DevHost.Demo.Terminal;

/// <summary>The SGR and control sequences the demo shell writes.</summary>
internal static class Ansi
{
	public const string Reset = "\u001b[0m";

	public const string Bold = "\u001b[1m";

	public const string Dim = "\u001b[2m";

	public const string Inverse = "\u001b[7m";

	public const string Red = "\u001b[31m";

	public const string Green = "\u001b[32m";

	public const string Yellow = "\u001b[33m";

	public const string Blue = "\u001b[34m";

	public const string Cyan = "\u001b[36m";

	public const string BoldRed = "\u001b[1;31m";

	public const string BoldGreen = "\u001b[1;32m";

	public const string BoldBlue = "\u001b[1;34m";

	public const string BoldCyan = "\u001b[1;36m";

	/// <summary>Erases from the cursor to the end of the line.</summary>
	public const string EraseLine = "\u001b[K";

	/// <summary>Homes the cursor and clears the screen, keeping the scrollback, like Ctrl+L in bash.</summary>
	public const string ClearScreen = "\u001b[H\u001b[2J";

	/// <summary>Also drops the scrollback, like the <c>clear</c> command.</summary>
	public const string ClearAll = "\u001b[H\u001b[2J\u001b[3J";

	/// <summary>Sets the window title, which the shell shows in the tab tooltip.</summary>
	public static string Title(string title) => "\u001b]0;" + title + "\u0007";
}
