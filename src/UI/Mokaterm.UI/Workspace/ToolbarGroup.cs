namespace Mokaterm.UI.Workspace;

/// <summary>The session toolbar's groups, in the order the grouped layout shows them.</summary>
public enum ToolbarGroup
{
	/// <summary>Panels docked beside the terminal: the file browser and saved commands.</summary>
	Panels,

	/// <summary>What the terminal shows: find, theme, clear.</summary>
	View,

	/// <summary>The session itself: recording, typing into several sessions, duplicating.</summary>
	Session,

	/// <summary>The views modules add.</summary>
	Tools,

	/// <summary>Reconnect, disconnect and close, which never fold away.</summary>
	Connection,
}
