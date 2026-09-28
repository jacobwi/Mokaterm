using Mokaterm.Abstractions.Presentation;

namespace Mokaterm.UI.Settings.Pages.Terminal;

/// <summary>Every color a terminal theme defines, grouped the way the theme editor lays them out.</summary>
internal static class ThemeColorFields
{
	public static IReadOnlyList<ThemeColorGroup> Groups { get; } =
	[
		new("Base",
		[
			new("Foreground", theme => theme.Foreground, (theme, value) => theme with { Foreground = value }),
			new("Background", theme => theme.Background, (theme, value) => theme with { Background = value }),
			new("Cursor", theme => theme.Cursor, (theme, value) => theme with { Cursor = value }),
			new("Cursor text", theme => theme.CursorAccent, (theme, value) => theme with { CursorAccent = value }),
			new("Selection",
				theme => theme.SelectionBackground,
				(theme, value) => theme with { SelectionBackground = value },
				HexColorForms.ThemeSlot),
			new("Selection text",
				theme => theme.SelectionForeground,
				(theme, value) => theme with { SelectionForeground = value.Length == 0 ? null : value },
				HexColorForms.ThemeSlot,
				Optional: true),
		]),
		new("Normal",
		[
			new("Black", theme => theme.Black, (theme, value) => theme with { Black = value }),
			new("Red", theme => theme.Red, (theme, value) => theme with { Red = value }),
			new("Green", theme => theme.Green, (theme, value) => theme with { Green = value }),
			new("Yellow", theme => theme.Yellow, (theme, value) => theme with { Yellow = value }),
			new("Blue", theme => theme.Blue, (theme, value) => theme with { Blue = value }),
			new("Magenta", theme => theme.Magenta, (theme, value) => theme with { Magenta = value }),
			new("Cyan", theme => theme.Cyan, (theme, value) => theme with { Cyan = value }),
			new("White", theme => theme.White, (theme, value) => theme with { White = value }),
		]),
		new("Bright",
		[
			new("Bright black", theme => theme.BrightBlack, (theme, value) => theme with { BrightBlack = value }),
			new("Bright red", theme => theme.BrightRed, (theme, value) => theme with { BrightRed = value }),
			new("Bright green", theme => theme.BrightGreen, (theme, value) => theme with { BrightGreen = value }),
			new("Bright yellow", theme => theme.BrightYellow, (theme, value) => theme with { BrightYellow = value }),
			new("Bright blue", theme => theme.BrightBlue, (theme, value) => theme with { BrightBlue = value }),
			new("Bright magenta", theme => theme.BrightMagenta, (theme, value) => theme with { BrightMagenta = value }),
			new("Bright cyan", theme => theme.BrightCyan, (theme, value) => theme with { BrightCyan = value }),
			new("Bright white", theme => theme.BrightWhite, (theme, value) => theme with { BrightWhite = value }),
		]),
	];
}
