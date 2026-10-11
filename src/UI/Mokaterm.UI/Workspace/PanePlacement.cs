namespace Mokaterm.UI.Workspace;

/// <summary>Where a restore puts the session it opens for a login: the pane at <paramref name="Position"/>.</summary>
internal readonly record struct PanePlacement(Guid ConnectionId, int Position);
