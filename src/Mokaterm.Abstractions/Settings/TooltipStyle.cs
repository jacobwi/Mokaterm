namespace Mokaterm.Abstractions.Settings;

/// <summary>How tooltips are drawn. Stored by name; new styles go last.</summary>
public enum TooltipStyle
{
	/// <summary>A raised box with a border and a soft shadow.</summary>
	Standard,

	/// <summary>A border and no shadow, with tighter corners.</summary>
	Flat,

	/// <summary>No visible border and a deep shadow.</summary>
	Elevated,

	/// <summary>See-through, with the page blurred behind it.</summary>
	Glass,

	/// <summary>A trace of the accent colour.</summary>
	AccentTint,

	/// <summary>Dark on a light page and lighter on a dark one.</summary>
	Contrast,

	/// <summary>Monospaced, with an accent border.</summary>
	Terminal,
}
