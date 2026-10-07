using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.UI.Tests.Fakes;

/// <summary>
/// A catalog holding one named theme. Editors only read a theme's name, so every color is the same and the name is what
/// a test checks for.
/// </summary>
internal sealed class OneTheme : ITerminalThemeCatalog
{
	private const string Ink = "#000000";

	private readonly TerminalTheme _theme;

	public OneTheme(string id, string name) =>
		_theme = new TerminalTheme
		{
			Id = id,
			Name = name,
			Foreground = Ink,
			Background = Ink,
			Cursor = Ink,
			CursorAccent = Ink,
			SelectionBackground = Ink,
			Black = Ink,
			Red = Ink,
			Green = Ink,
			Yellow = Ink,
			Blue = Ink,
			Magenta = Ink,
			Cyan = Ink,
			White = Ink,
			BrightBlack = Ink,
			BrightRed = Ink,
			BrightGreen = Ink,
			BrightYellow = Ink,
			BrightBlue = Ink,
			BrightMagenta = Ink,
			BrightCyan = Ink,
			BrightWhite = Ink,
		};

	public TerminalTheme Default => _theme;

	public IReadOnlyList<TerminalTheme> Themes => [_theme];

	/// <summary>The one theme, whatever is asked for: an id nothing matches resolves to the default anyway.</summary>
	public TerminalTheme Get(string? id) => _theme;
}
