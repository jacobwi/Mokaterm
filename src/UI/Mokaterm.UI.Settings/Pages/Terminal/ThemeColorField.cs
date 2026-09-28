using Mokaterm.Abstractions.Presentation;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.UI.Settings.Pages.Terminal;

/// <summary>One editable color of a <see cref="TerminalTheme"/>.</summary>
/// <param name="Set">Receives a validated color, or an empty string when an <paramref name="Optional"/> color is cleared.</param>
/// <param name="Forms">
/// Which spellings this slot takes. Alpha belongs to the slots that are drawn over whatever is behind them, which is
/// why every other one stays opaque.
/// </param>
internal sealed record ThemeColorField(
	string Label,
	Func<TerminalTheme, string?> Get,
	Func<TerminalTheme, string, TerminalTheme> Set,
	HexColorForms Forms = HexColorForms.Opaque,
	bool Optional = false);

/// <summary>Colors shown together under one heading in the theme editor.</summary>
internal sealed record ThemeColorGroup(string Title, IReadOnlyList<ThemeColorField> Fields);
