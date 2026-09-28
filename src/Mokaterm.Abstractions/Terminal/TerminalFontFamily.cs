using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Mokaterm.Abstractions.Terminal;

/// <summary>
/// The one rule for terminal font lists, the global one and a connection's own. A list lands in style attributes and in
/// the style element xterm.js writes, so it may not hold a character that ends the declaration, opens a rule, calls a
/// CSS function or starts an escape. Editors refuse such a list, stored settings fall back to the default, and the
/// terminal strips what is left of it before use.
/// </summary>
public static class TerminalFontFamily
{
	/// <summary>The rule as a short hint for a font field.</summary>
	public const string Requirement = "Font lists can't contain ; { } ( ) < > \\ or line breaks.";

	private static readonly SearchValues<char> Forbidden = SearchValues.Create(";{}()<>\\");

	/// <summary>True for a list with something in it and none of the characters the rule forbids.</summary>
	public static bool IsValid([NotNullWhen(true)] string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}

		foreach (char character in value)
		{
			if (IsForbidden(character))
			{
				return false;
			}
		}

		return true;
	}

	/// <summary><paramref name="value"/> without the characters the rule forbids, trimmed. Empty when nothing is left.</summary>
	public static string Strip(string? value)
	{
		if (value is null)
		{
			return "";
		}

		StringBuilder kept = new(value.Length);
		foreach (char character in value)
		{
			if (!IsForbidden(character))
			{
				kept.Append(character);
			}
		}

		return kept.ToString().Trim();
	}

	// Control characters include CR and LF, and a form feed or NUL has no business in a font name either.
	private static bool IsForbidden(char character) => char.IsControl(character) || Forbidden.Contains(character);
}
