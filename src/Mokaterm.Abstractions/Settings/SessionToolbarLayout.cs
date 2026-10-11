namespace Mokaterm.Abstractions.Settings;

/// <summary>How the bar above a session arranges its buttons. Stored by name; new layouts go last.</summary>
public enum SessionToolbarLayout
{
	/// <summary>Panels, view, session, a menu of tools, then disconnect and close, with a line between the groups.</summary>
	Grouped,

	/// <summary>Every button in one row, each tool with a button of its own.</summary>
	Flat,

	/// <summary>The session's state and one menu that holds everything.</summary>
	Compact,
}
