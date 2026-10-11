using Mokaterm.Abstractions.Settings;

namespace Mokaterm.UI.Terminal.Triggers;

/// <summary>
/// The colors a trigger paints with: Moka's status colors, fixed rather than taken from the terminal theme, so red means
/// the same in every theme. Plain <c>#rrggbb</c>, because that is all an xterm.js decoration takes.
/// </summary>
public static class TriggerPalette
{
	/// <summary>Every color a rule can pick, in the order the editor offers them.</summary>
	public static IReadOnlyList<TriggerColor> Colors { get; } =
	[
		TriggerColor.Red,
		TriggerColor.Orange,
		TriggerColor.Yellow,
		TriggerColor.Green,
		TriggerColor.Blue,
		TriggerColor.Purple,
	];

	/// <summary>The color as <c>#rrggbb</c>, or null for <see cref="TriggerColor.None"/> and anything undefined.</summary>
	public static string? Hex(TriggerColor color) => color switch
	{
		TriggerColor.Red => "#ef5350",
		TriggerColor.Orange => "#ffab40",
		TriggerColor.Yellow => "#ffd54f",
		TriggerColor.Green => "#00e676",
		TriggerColor.Blue => "#42a5f5",
		TriggerColor.Purple => "#ce93d8",
		_ => null,
	};

	public static string Name(TriggerColor color) => color switch
	{
		TriggerColor.Red => "Red",
		TriggerColor.Orange => "Orange",
		TriggerColor.Yellow => "Yellow",
		TriggerColor.Green => "Green",
		TriggerColor.Blue => "Blue",
		TriggerColor.Purple => "Purple",
		_ => "None",
	};
}
