using Microsoft.AspNetCore.Components;
using Moka.Red.Navigation.Tabs;

namespace Mokaterm.UI.Common.Components;

/// <summary>
/// A row of tabs that switch the view below them: the Docker manager's container and project panels, the Plugins page.
/// It is Moka's <see cref="MokaTabs"/> sized for a tool page, so the arrow keys, Home and End move between tabs and
/// show the one they land on. The parent draws the view for <see cref="Active"/>, either below the tabs or in
/// <see cref="Panel"/>, which also names it by its tab.
/// </summary>
public sealed partial class ViewTabs : ComponentBase
{
	private IReadOnlyList<MokaTabItem> _items = [];

	/// <summary>Each tab's id and the text it shows. <see cref="Items"/> wins when both are set.</summary>
	[Parameter]
	public IReadOnlyList<(string Id, string Text)> Tabs { get; set; } = [];

	/// <summary>Tabs with a count, an icon or a state that cannot be picked, in place of <see cref="Tabs"/>.</summary>
	[Parameter]
	public IReadOnlyList<MokaTabItem>? Items { get; set; }

	[Parameter]
	public string Active { get; set; } = "";

	[Parameter]
	public EventCallback<string> ActiveChanged { get; set; }

	/// <summary>What the tabs choose between, for screen readers: "Container views".</summary>
	[Parameter]
	public string Label { get; set; } = "Views";

	/// <summary>
	/// The showing tab's view, drawn under the tabs as their tab panel and named by the tab. Leave it out to draw the view
	/// anywhere, such as in a column that has to fill the rest of a panel.
	/// </summary>
	[Parameter]
	public RenderFragment? Panel { get; set; }

	protected override void OnParametersSet() =>
		_items = Items ?? [.. Tabs.Select(tab => new MokaTabItem(tab.Id, tab.Text))];
}
