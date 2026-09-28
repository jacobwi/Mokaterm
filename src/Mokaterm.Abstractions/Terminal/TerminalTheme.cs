namespace Mokaterm.Abstractions.Terminal;

/// <summary>A terminal color scheme. Colors are <c>#rrggbb</c>, or <c>#rrggbbaa</c> for selection.</summary>
public sealed record TerminalTheme
{
	public required string Id { get; init; }

	public required string Name { get; init; }

	public bool IsDark { get; init; } = true;

	/// <summary>False for themes shipped with the app, true for themes the user created.</summary>
	public bool IsCustom { get; init; }

	public required string Foreground { get; init; }

	public required string Background { get; init; }

	public required string Cursor { get; init; }

	public required string CursorAccent { get; init; }

	public required string SelectionBackground { get; init; }

	public string? SelectionForeground { get; init; }

	public required string Black { get; init; }

	public required string Red { get; init; }

	public required string Green { get; init; }

	public required string Yellow { get; init; }

	public required string Blue { get; init; }

	public required string Magenta { get; init; }

	public required string Cyan { get; init; }

	public required string White { get; init; }

	public required string BrightBlack { get; init; }

	public required string BrightRed { get; init; }

	public required string BrightGreen { get; init; }

	public required string BrightYellow { get; init; }

	public required string BrightBlue { get; init; }

	public required string BrightMagenta { get; init; }

	public required string BrightCyan { get; init; }

	public required string BrightWhite { get; init; }
}

/// <summary>Built-in themes plus the user's custom themes from <see cref="Settings.TerminalSettings.CustomThemes"/>.</summary>
public interface ITerminalThemeCatalog
{
	/// <summary>The theme used when a saved id no longer exists.</summary>
	TerminalTheme Default { get; }

	/// <summary>Built-in themes first, then custom themes. Reflects settings changes immediately.</summary>
	IReadOnlyList<TerminalTheme> Themes { get; }

	/// <summary>The theme with <paramref name="id"/>, or <see cref="Default"/>.</summary>
	TerminalTheme Get(string? id);
}
