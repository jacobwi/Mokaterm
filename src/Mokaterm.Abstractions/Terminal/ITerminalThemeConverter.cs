namespace Mokaterm.Abstractions.Terminal;

/// <summary>Reads color schemes other terminals export, and writes themes back out for them.</summary>
public interface ITerminalThemeConverter
{
	/// <summary>
	/// Reads the first color scheme in <paramref name="text"/>, detecting its format from the content and, when given,
	/// the <paramref name="fileName"/> extension. The theme comes back as a custom theme with a new id, named after the
	/// scheme or the file.
	/// </summary>
	TerminalThemeImport Import(string text, string? fileName = null);

	/// <summary>Writes <paramref name="theme"/> in <paramref name="format"/>, ready to paste into that terminal's configuration.</summary>
	string Export(TerminalTheme theme, TerminalThemeFormat format);
}
