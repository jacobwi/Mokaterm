using Mokaterm.UI.Common.Contributions;

namespace Mokaterm.UI.Shell;

/// <summary>Ids shared by palette commands, keyboard shortcuts and the cheat sheet.</summary>
internal static class ShellCommandIds
{
	public const string CommandPalette = "mokaterm.palette";

	public const string NewHost = "mokaterm.host.new";

	public const string NewLogin = "mokaterm.login.new";

	public const string NewFolder = "mokaterm.folder.new";

	public const string ImportConnections = "mokaterm.connections.import";

	public const string ExportConnections = "mokaterm.connections.export";

	public const string QuickConnect = "mokaterm.quick-connect";

	public const string LockVault = "mokaterm.vault.lock";

	public const string Settings = "mokaterm.settings";

	public const string ToggleConnections = "mokaterm.panel.connections";

	public const string ToggleTransfers = "mokaterm.panel.transfers";

	public const string NextTab = "mokaterm.tab.next";

	public const string PreviousTab = "mokaterm.tab.previous";

	public const string CloseTab = "mokaterm.tab.close";

	public const string CloseAllTabs = "mokaterm.tab.close-all";

	public const string Reconnect = "mokaterm.session.reconnect";

	public const string Disconnect = "mokaterm.session.disconnect";

	public const string Duplicate = "mokaterm.session.duplicate";

	public const string OpenFileBrowser = "mokaterm.session.file-browser";

	public const string SavedCommands = "mokaterm.session.commands";

	public const string BroadcastInput = "mokaterm.session.broadcast";

	public const string MoveToOtherPane = "mokaterm.tab.other-pane";

	public const string Shortcuts = "mokaterm.shortcuts";

	public const string FocusPane1 = "mokaterm.pane.focus-1";

	public const string FocusPane2 = "mokaterm.pane.focus-2";

	public const string FocusPane3 = "mokaterm.pane.focus-3";

	public const string FocusPane4 = "mokaterm.pane.focus-4";

	public const string MoveToPane1 = "mokaterm.tab.pane-1";

	public const string MoveToPane2 = "mokaterm.tab.pane-2";

	public const string MoveToPane3 = "mokaterm.tab.pane-3";

	public const string MoveToPane4 = "mokaterm.tab.pane-4";

	public const string NewPaneRight = "mokaterm.tab.new-pane-right";

	public const string NewPaneDown = "mokaterm.tab.new-pane-down";

	public const string NewPaneLeft = "mokaterm.tab.new-pane-left";

	public const string NewPaneUp = "mokaterm.tab.new-pane-up";

	public const string SinglePane = "mokaterm.panes.single";

	public const string LayoutSideBySide = "mokaterm.panes.side-by-side";

	public const string LayoutStacked = "mokaterm.panes.stacked";

	public const string LayoutThree = "mokaterm.panes.three";

	public const string LayoutGrid = "mokaterm.panes.grid";

	public const string RunCommand = "mokaterm.commands.run";

	public const string ToggleFileBrowser = "mokaterm.session.toggle-files";

	public const string FindInTerminal = "mokaterm.session.find";

	public const string TerminalTheme = "mokaterm.session.theme";

	public const string ClearTerminal = "mokaterm.session.clear";

	public const string RecordSession = "mokaterm.session.record";

	/// <summary>Opening a session tool, followed by the tool's key, such as <c>mokaterm.tool.docker</c>.</summary>
	public const string ToolPrefix = "mokaterm.tool.";

	/// <summary>The command that opens <paramref name="tool"/> for the focused session.</summary>
	public static string ForTool(SessionToolDescriptor tool)
	{
		ArgumentNullException.ThrowIfNull(tool);
		return ToolPrefix + tool.Key;
	}

	/// <summary>The focus command for each pane, by position.</summary>
	public static IReadOnlyList<string> FocusPane { get; } = [FocusPane1, FocusPane2, FocusPane3, FocusPane4];

	/// <summary>The move command for each pane, by position.</summary>
	public static IReadOnlyList<string> MoveToPane { get; } = [MoveToPane1, MoveToPane2, MoveToPane3, MoveToPane4];
}
