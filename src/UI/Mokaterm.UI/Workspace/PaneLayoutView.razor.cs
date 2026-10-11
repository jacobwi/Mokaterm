using Microsoft.AspNetCore.Components;

namespace Mokaterm.UI.Workspace;

/// <summary>
/// Draws a pane tree as nested splitters and leaves each pane's content to <see cref="Pane"/>. Apart from the workspace
/// so a layout can be rendered, and tested, with no session behind it.
/// </summary>
/// <remarks>
/// Every split is keyed on its id, so one that survives a change keeps the size it was dragged to. A pane that moves to
/// another place in the tree is a new component there: Blazor cannot carry a component from one parent to another, so its
/// views mount again, and a terminal redraws from its replay buffer.
/// </remarks>
public sealed partial class PaneLayoutView : ComponentBase
{
	// Enough for a tab strip, the session toolbar and a few rows or columns of terminal.
	private const string MinWidth = "260px";

	private const string MinHeight = "160px";

	private static readonly RenderFragment Nothing = static _ => { };

	[Parameter]
	[EditorRequired]
	public PaneNode Layout { get; set; } = new PaneLeaf(0);

	/// <summary>
	/// The content of one pane, given its id. Key the content on that id, so a pane is never mistaken for the one that
	/// stood in its place before.
	/// </summary>
	[Parameter]
	[EditorRequired]
	public RenderFragment<int>? Pane { get; set; }
}
