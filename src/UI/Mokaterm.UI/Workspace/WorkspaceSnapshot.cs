namespace Mokaterm.UI.Workspace;

/// <summary>Every tab in tab order with its pane, and the layout those panes are in, read at one moment.</summary>
internal sealed record WorkspaceSnapshot(IReadOnlyList<PanedTab> Tabs, PaneNode Layout);
