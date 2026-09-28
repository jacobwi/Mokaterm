using System.Diagnostics.CodeAnalysis;

namespace Mokaterm.Abstractions.Presentation;

/// <summary>
/// The one rule for a hex color: which spellings a value may use, what it looks like once it is stored, and what to tell
/// someone who typed something else. The settings sanitizer, the color fields, the theme and the host colors all ask
/// here, so a color one of them keeps cannot be one another refuses.
/// </summary>
public static class HexColor
{
	private const int ShortLength = 4;

	private const int OpaqueLength = 7;

	private const int AlphaLength = 9;

	/// <summary>True when <paramref name="value"/> is exactly one of the spellings <paramref name="forms"/> allows.</summary>
	public static bool IsValid([NotNullWhen(true)] string? value, HexColorForms forms)
	{
		if (value is null || !Allows(forms, value.Length) || value[0] != '#')
		{
			return false;
		}

		foreach (char character in value.AsSpan(1))
		{
			if (!char.IsAsciiHexDigit(character))
			{
				return false;
			}
		}

		return true;
	}

	/// <summary>
	/// <paramref name="value"/> trimmed, and expanded to <c>#rrggbb</c> when it was written short, which is the form that
	/// gets stored. Null when it is blank or not a color <paramref name="forms"/> allows.
	/// </summary>
	public static string? Normalize(string? value, HexColorForms forms)
	{
		string? trimmed = value?.Trim();
		return !IsValid(trimmed, forms) ? null : trimmed.Length == ShortLength ? Expand(trimmed) : trimmed;
	}

	/// <summary>The spellings <paramref name="forms"/> allows, as the sentence a field shows when it refuses text.</summary>
	public static string Requirement(HexColorForms forms)
	{
		List<string> spellings = new(3);
		if ((forms & HexColorForms.Compact) != 0)
		{
			spellings.Add("#rgb");
		}

		if ((forms & HexColorForms.Opaque) != 0)
		{
			spellings.Add("#rrggbb");
		}

		if ((forms & HexColorForms.Alpha) != 0)
		{
			spellings.Add("#rrggbbaa");
		}

		// Allowing nothing is a caller mistake, and naming the one form everything takes beats an empty sentence.
		return spellings.Count == 0 ? "Use #rrggbb." : "Use " + string.Join(" or ", spellings) + ".";
	}

	private static bool Allows(HexColorForms forms, int length) => length switch
	{
		ShortLength => (forms & HexColorForms.Compact) != 0,
		OpaqueLength => (forms & HexColorForms.Opaque) != 0,
		AlphaLength => (forms & HexColorForms.Alpha) != 0,
		_ => false,
	};

	private static string Expand(string shortForm) => string.Create(OpaqueLength, shortForm, static (span, value) =>
	{
		span[0] = '#';
		for (int index = 0; index < 3; index++)
		{
			span[1 + (index * 2)] = value[1 + index];
			span[2 + (index * 2)] = value[1 + index];
		}
	});
}
