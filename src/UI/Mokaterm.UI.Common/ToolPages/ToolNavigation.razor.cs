using Microsoft.AspNetCore.Components;

namespace Mokaterm.UI.Common.ToolPages;

/// <summary>
/// The side menu of a paged tool: the System view, the Docker manager, and whatever pages plugins add to them. Pages
/// are named by id, so a page that cannot be used here still opens and says why rather than disappearing.
/// </summary>
public sealed partial class ToolNavigation : ComponentBase
{
	private IReadOnlyList<string> _pages = [];

	[Parameter, EditorRequired]
	public IReadOnlyList<ToolNavEntry> Entries { get; set; } = [];

	/// <summary>The id of the page on show.</summary>
	[Parameter]
	public string Current { get; set; } = "";

	[Parameter]
	public EventCallback<string> CurrentChanged { get; set; }

	/// <summary>Names the navigation and the dropdown that replaces it on a narrow tool.</summary>
	[Parameter, EditorRequired]
	public string Label { get; set; } = "";

	/// <summary>The current page.</summary>
	[Parameter]
	public RenderFragment? ChildContent { get; set; }

	protected override void OnParametersSet() => _pages = [.. Entries.Select(entry => entry.Id)];

	private bool IsCurrent(ToolNavEntry entry) => string.Equals(entry.Id, Current, StringComparison.Ordinal);

	private string ItemClass(ToolNavEntry entry) =>
		"mt-toolnav-item" + (IsCurrent(entry) ? " mt-toolnav-item--active" : "") + (entry.Unavailable is not null ? " mt-toolnav-item--off" : "");

	private string SelectLabel(string id) =>
		Entries.FirstOrDefault(entry => string.Equals(entry.Id, id, StringComparison.Ordinal)) is { } entry
			? entry.Unavailable is null ? entry.Label : $"{entry.Label} (not on this system)"
			: id;
}
