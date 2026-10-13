using Mokaterm.UI.Common.Platform;

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

	/// <summary>
	/// The pane each of <see cref="RestoreConnectionIds"/> was in, counted from 0 in <see cref="RestoreLayout"/>'s order.
	/// Empty when everything was in one pane.
	/// </summary>
	public IReadOnlyList<int> RestorePanes { get; init; } = [];

	/// <summary>
	/// The shape of the panes those tabs were in, such as <c>H(P,V(P,P))</c>, with no ids in it. Null for one pane.
	/// </summary>
	public string? RestoreLayout { get; init; }

	/// <summary>
	/// Where each tool's own window was last and whether it stayed on top, by <see cref="ToolWindowTarget.PlaceKey"/>
	/// (<c>docker</c>, <c>app:subnet-calculator</c>), so the next one opens the same way. One entry per tool rather than
	/// per session: the key names a module's view, never a machine.
	/// </summary>
	public IReadOnlyDictionary<string, ToolWindowBounds> ToolWindows { get; init; } =
		new Dictionary<string, ToolWindowBounds>(StringComparer.Ordinal);

	/// <summary>The tool windows that were open, in the order they opened, for the next start to bring back.</summary>
	public IReadOnlyList<ToolWindowMemo> RestoreToolWindows { get; init; } = [];
}
