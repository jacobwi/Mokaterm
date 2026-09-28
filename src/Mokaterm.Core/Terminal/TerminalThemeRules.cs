using System.Diagnostics.CodeAnalysis;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.Core.Terminal;

/// <summary>
/// The one rule for whether a custom theme is usable. The settings sanitizer drops themes that fail it and the
/// importer never produces one.
/// </summary>
internal static class TerminalThemeRules
{
	/// <summary>True when every field the terminal view needs is filled in.</summary>
	public static bool IsComplete([NotNullWhen(true)] TerminalTheme? theme)
	{
		if (theme is null)
		{
			return false;
		}

		string?[] required =
		[
			theme.Id, theme.Name, theme.Foreground, theme.Background, theme.Cursor, theme.CursorAccent, theme.SelectionBackground,
			theme.Black, theme.Red, theme.Green, theme.Yellow, theme.Blue, theme.Magenta, theme.Cyan, theme.White,
			theme.BrightBlack, theme.BrightRed, theme.BrightGreen, theme.BrightYellow, theme.BrightBlue, theme.BrightMagenta, theme.BrightCyan, theme.BrightWhite,
		];

		return Array.TrueForAll(required, value => !string.IsNullOrWhiteSpace(value));
	}
}
