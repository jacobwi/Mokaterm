using Mokaterm.Abstractions.Sessions;

namespace Mokaterm.UI.Workspace;

/// <summary>A tab and the pane it sits in.</summary>
internal sealed record PanedTab(ISessionHandle Session, int PaneId);
