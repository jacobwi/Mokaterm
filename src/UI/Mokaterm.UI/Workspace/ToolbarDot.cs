namespace Mokaterm.UI.Workspace;

/// <summary>A small dot on a toolbar icon that says something is going on behind it.</summary>
public enum ToolbarDot
{
	None,

	/// <summary>Red: a file is being written, such as a session recording.</summary>
	Recording,

	/// <summary>Amber: on, but not working as asked, such as a recording that is full or failed.</summary>
	Warning,
}
