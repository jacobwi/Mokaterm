using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;
using Mokaterm.Abstractions.Presentation;

namespace Mokaterm.UI.Common.Components;

/// <summary>A hex color field that saves only complete colors; MokaColorInput itself accepts any text.</summary>
public sealed partial class ColorSetting : DraftSettingBase<string>
{
	private const int HexWithAlphaLength = 9;

	/// <summary>Which spellings this field takes. <see cref="HexColorForms.Opaque"/> is <c>#rrggbb</c> and nothing else.</summary>
	[Parameter]
	public HexColorForms Forms { get; set; } = HexColorForms.Opaque;

	/// <summary>Saves an empty string when cleared, for colors that fall back to a default.</summary>
	[Parameter]
	public bool AllowEmpty { get; set; }

	// The browser's picker only produces #rrggbb: it would drop an alpha channel, and it shows black for an unset
	// optional color. The plain preview swatch stands in for it in both cases.
	private bool ShowNativeInput =>
		!((Forms & HexColorForms.Alpha) != 0 && Text.Length == HexWithAlphaLength) && !(AllowEmpty && Text.Length == 0);

	/// <summary>
	/// What this field saves for <paramref name="text"/>, or null when it refuses it. Normalize rather than IsValid: a
	/// color typed short is saved in the form everything else stores, which is also the only form the browser's own
	/// picker can show.
	/// </summary>
	internal static string? Parse(string text, HexColorForms forms, bool allowEmpty) =>
		HexColor.Normalize(text, forms) ?? (allowEmpty && text.Trim().Length == 0 ? "" : null);

	protected override bool TryParse(string text, [MaybeNullWhen(false)] out string value, [NotNullWhen(false)] out string? error)
	{
		if (Parse(text, Forms, AllowEmpty) is { } color)
		{
			value = color;
			error = null;
			return true;
		}

		value = null;
		error = HexColor.Requirement(Forms);
		return false;
	}

	protected override string Format(string value) => value;
}
