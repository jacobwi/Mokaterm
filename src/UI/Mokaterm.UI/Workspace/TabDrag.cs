namespace Mokaterm.UI.Workspace;

/// <summary>
/// The tab a strip is dragging, shared by the panes so a tab can be dropped on another pane than its own. HTML5
/// drag-and-drop between components carries no payload Blazor can read, so the strip's own drag is reported through a
/// line of JavaScript (shell.js attachTabDrag) and the panes read it from here. Scoped to the window, like the
/// workspace it serves.
/// </summary>
public sealed class TabDrag
{
	private readonly object _gate = new();

	/// <summary>The session whose tab is being dragged, or null when nothing is.</summary>
	public Guid? SessionId { get; private set; }

	/// <summary>The pane the drag started in, so its own panes do not treat their reorder as foreign.</summary>
	public int? SourcePane { get; private set; }

	public void Start(Guid sessionId, int sourcePane)
	{
		lock (_gate)
		{
			SessionId = sessionId;
			SourcePane = sourcePane;
		}
	}

	public void End()
	{
		lock (_gate)
		{
			SessionId = null;
			SourcePane = null;
		}
	}

	/// <summary>True while a tab from another pane is being dragged over the pane with this id.</summary>
	public bool IsForeignDrag(int paneId)
	{
		lock (_gate)
		{
			return SessionId is not null && SourcePane != paneId;
		}
	}
}
