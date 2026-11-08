namespace Mokaterm.UI.Workspace;

/// <summary>Neighbouring buttons of one toolbar group, which leave the bar together when it runs out of room.</summary>
/// <param name="Divider">Draws a line before the run, which starts a group.</param>
/// <param name="Priority">Moka's drop order for the run: the lowest leaves first.</param>
internal sealed record ToolbarRun(IReadOnlyList<ToolbarAction> Actions, bool Divider, int Priority);
