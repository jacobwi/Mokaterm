namespace Mokaterm.UI.Workspace;

/// <summary>View state that belongs to one session and must survive its view being unmounted by a vault lock.</summary>
internal sealed class SessionViewState
{
	public bool FilesCollapsed { get; set; }

	/// <summary>Whether the saved commands panel is docked open beside the terminal.</summary>
	public bool CommandsOpen { get; set; }

	/// <summary>A theme picked for this session only, on top of the connection's and the global one.</summary>
	public string? ThemeId { get; set; }

	/// <summary>The window title the remote side last set, shown in the tab tooltip.</summary>
	public string? RemoteTitle { get; set; }
}
