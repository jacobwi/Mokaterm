using System.Globalization;
using System.Text.RegularExpressions;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.UI.Terminal.Internal;

/// <summary>
/// Makes user-controlled theme and font values safe to place in a style attribute. Custom themes can come from
/// imported files, so a value must not be able to add declarations such as <c>background: url(...)</c>.
/// </summary>
internal static partial class CssValues
{
	public const string DefaultFontFamily = "monospace";

	/// <summary>The value when it is a <c>#rgb</c>, <c>#rgba</c>, <c>#rrggbb</c> or <c>#rrggbbaa</c> color, otherwise null.</summary>
	public static string? Color(string? value) =>
		value is not null && HexColor().IsMatch(value) ? value : null;

	/// <summary>
	/// A font-family list without the characters <see cref="TerminalFontFamily"/> forbids. Stored lists are already clean;
	/// this is the last line in case one is not.
	/// </summary>
	public static string FontFamily(string? value)
	{
		string cleaned = TerminalFontFamily.Strip(value);
		return cleaned.Length == 0 ? DefaultFontFamily : cleaned;
	}

	public static string Pixels(double value) =>
		value.ToString("0.##", CultureInfo.InvariantCulture) + "px";

	[GeneratedRegex("^#(?:[0-9a-fA-F]{3,4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$")]
	private static partial Regex HexColor();
}
