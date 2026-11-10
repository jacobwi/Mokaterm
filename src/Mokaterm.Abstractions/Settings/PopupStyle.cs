namespace Mokaterm.Abstractions.Settings;

/// <summary>How menus, popovers and other floating panels are drawn. Stored by name; new styles go last.</summary>
public enum PopupStyle
{
	/// <summary>The theme's own panel.</summary>
	Standard,

	/// <summary>A border and no shadow.</summary>
	Flat,

	/// <summary>A deep shadow.</summary>
	Elevated,

	/// <summary>See-through, with the page blurred behind it.</summary>
	Glass,

	/// <summary>A trace of the accent colour.</summary>
	AccentTint,

	/// <summary>Stands out from the page.</summary>
	Contrast,

	/// <summary>Monospaced, with an accent border.</summary>
	Terminal,
}
