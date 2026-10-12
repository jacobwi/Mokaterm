using Microsoft.AspNetCore.Components;

namespace Mokaterm.Modules.Ssh.Components;

public sealed partial class ToolNavigation<TPage> : ComponentBase
{
	private IReadOnlyList<TPage> _pages = [];

	[Parameter, EditorRequired]
	public IReadOnlyList<ToolNavEntry<TPage>> Entries { get; set; } = [];

	[Parameter]
	public TPage Current { get; set; } = default!;

	[Parameter]
	public EventCallback<TPage> CurrentChanged { get; set; }

	/// <summary>Names the navigation and the dropdown that replaces it on a narrow tool.</summary>
	[Parameter, EditorRequired]
	public string Label { get; set; } = "";

	/// <summary>The current page.</summary>
	[Parameter]
	public RenderFragment? ChildContent { get; set; }

	protected override void OnParametersSet() => _pages = [.. Entries.Select(entry => entry.Page)];

	private bool IsCurrent(ToolNavEntry<TPage> entry) => EqualityComparer<TPage>.Default.Equals(entry.Page, Current);

	private string ItemClass(ToolNavEntry<TPage> entry) =>
		"mt-toolnav-item" + (IsCurrent(entry) ? " mt-toolnav-item--active" : "") + (entry.Unavailable is not null ? " mt-toolnav-item--off" : "");

	private string SelectLabel(TPage page) =>
		Entries.FirstOrDefault(entry => EqualityComparer<TPage>.Default.Equals(entry.Page, page)) is { } entry
			? entry.Unavailable is null ? entry.Label : $"{entry.Label} (not on this system)"
			: page?.ToString() ?? "";
}
