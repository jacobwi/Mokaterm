namespace Mokaterm.UI.Workspace;

/// <summary>
/// Two nodes sharing a space, <paramref name="First"/> on the left or on top. The view keys its splitter on
/// <paramref name="Id"/>, so a split that survives a change keeps the size it was dragged to.
/// </summary>
public sealed record PaneSplit(int Id, PaneAxis Axis, PaneNode First, PaneNode Second) : PaneNode;
