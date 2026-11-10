namespace Mokaterm.Abstractions.Settings;

/// <summary>How toasts are drawn. Stored by name; new styles go last.</summary>
public enum ToastStyle
{
	/// <summary>The theme's own toast.</summary>
	Standard,

	/// <summary>An edge in the severity's colour.</summary>
	ToneEdge,

	/// <summary>Tinted in the severity's colour.</summary>
	Tinted,

	/// <summary>The icon in a tile.</summary>
	IconTile,

	/// <summary>A dark bar along the bottom edge.</summary>
	Snackbar,

	/// <summary>A small rounded pill.</summary>
	Pill,

	/// <summary>A monospaced line.</summary>
	Terminal,
}
