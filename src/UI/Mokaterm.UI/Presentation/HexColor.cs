using Mokaterm.Abstractions.Presentation;

namespace Mokaterm.UI.Presentation;

/// <summary>
/// The accent and host colors the shell draws. This is where the shell pins which spellings it takes, so the explorer,
/// the tabs and the host editor cannot each pick their own; <see cref="HexColorForms.Accent"/> is the rule itself.
/// </summary>
internal static class HexColor
{
	/// <inheritdoc cref="Abstractions.Presentation.HexColor.Normalize"/>
	public static string? Normalize(string? value) =>
		Abstractions.Presentation.HexColor.Normalize(value, HexColorForms.Accent);
}
