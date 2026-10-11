namespace Mokaterm.UI.Workspace;

/// <summary>
/// One node of the workspace's pane tree: a pane, or a split holding two nodes. Nodes never change, so a change to the
/// layout builds a new tree that shares every subtree it left alone. Public because the layout view takes one.
/// </summary>
public abstract record PaneNode;
