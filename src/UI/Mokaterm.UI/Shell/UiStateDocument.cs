namespace Mokaterm.UI.Shell;

/// <summary>
/// Layout state stored in the plain <c>ui-state</c> document. That file is not encrypted, so it holds ids and numbers
/// only, never names or addresses.
/// </summary>
internal sealed record UiStateDocument
{
	public IReadOnlyList<Guid> ExpandedFolderIds { get; init; } = [];

	public IReadOnlyList<Guid> ExpandedHostIds { get; init; } = [];

	public bool ConnectionsPanelCollapsed { get; init; }

	public double? ConnectionsPanelWidth { get; init; }

	public double? TransfersPanelHeight { get; init; }

	public double? FilesPanelWidth { get; init; }

	public double? CommandsPanelWidth { get; init; }

	/// <summary>The logins that had a tab, in tab order, for the next start to offer.</summary>
	public IReadOnlyList<Guid> RestoreConnectionIds { get; init; } = [];
}
