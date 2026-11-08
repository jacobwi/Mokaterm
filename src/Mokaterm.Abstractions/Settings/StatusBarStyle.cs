namespace Mokaterm.Abstractions.Settings;

/// <summary>How the status bar along the bottom of the window is drawn. Stored by name; new styles go last.</summary>
public enum StatusBarStyle
{
	/// <summary>A filled bar with a line above it.</summary>
	Standard,

	/// <summary>The whole bar in the accent colour.</summary>
	Accent,

	/// <summary>No fill, a thin line above and muted text.</summary>
	Hairline,

	/// <summary>Lifted off the window's edges with rounded corners.</summary>
	Floating,

	/// <summary>No bar, each item its own small outlined chip.</summary>
	Chips,

	/// <summary>The items joined into segments.</summary>
	Segments,

	/// <summary>A monospaced line, like a terminal's own status line.</summary>
	Terminal,
}
