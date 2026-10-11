namespace Mokaterm.UI.Workspace;

/// <summary>A pane. Its id stays with it for as long as it holds tabs and is never given to another pane.</summary>
public sealed record PaneLeaf(int PaneId) : PaneNode;
