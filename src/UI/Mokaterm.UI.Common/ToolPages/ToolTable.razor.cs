using Microsoft.AspNetCore.Components;
using Moka.Red.Core.Icons;
using Moka.Red.Core.Interactions;
using Moka.Red.Data.Table;

namespace Mokaterm.UI.Common.ToolPages;

/// <summary>
/// The list of a tool page: a <see cref="MokaTable{TItem}"/> that fills the frame's list area with its header fixed, no
/// toolbar or paging of its own (the page header filters), the row whose details are open drawn as selected, and the row
/// menu on a right-click. Columns go in as <c>MokaColumn</c>s and every row is one line: the column with the class
/// <c>mt-col-main</c> takes the width the others leave and cuts its text, and the rest are as wide as what they show
/// (<c>mt-col-dot</c> is the narrow one a row's state dot sits in). A
/// column with <c>mt-col-wide</c> leaves when the list is narrower than 560px, and one with <c>mt-col-wider</c> below
/// 800px.
/// </summary>
/// <typeparam name="TItem">What one row shows.</typeparam>
public partial class ToolTable<TItem>
{
	// Every row is exactly this high, menu button or not, which is what lets a virtualized list scroll true whatever the
	// theme's density does to Moka's own cell padding. The style below hands the same number to the CSS.
	private const int RowPixels = 36;

	private static readonly string RowStyle = $"--mt-tool-row: {RowPixels.ToString(System.Globalization.CultureInfo.InvariantCulture)}px";

	private HashSet<TItem>? _selected;

	/// <summary>The rows, already filtered, in the page's own order until the user sorts by a column.</summary>
	[Parameter, EditorRequired]
	public IReadOnlyList<TItem> Items { get; set; } = [];

	/// <summary>A stable key per row, so a refresh keeps focus and scroll where they were.</summary>
	[Parameter]
	public Func<TItem, object>? ItemKey { get; set; }

	/// <summary>What the table is called for assistive technology, such as "Services".</summary>
	[Parameter, EditorRequired]
	public string Label { get; set; } = "";

	/// <summary>The columns.</summary>
	[Parameter]
	public RenderFragment? ChildContent { get; set; }

	/// <summary>Which row has its details open; it is drawn as selected.</summary>
	[Parameter]
	public Func<TItem, bool>? IsSelected { get; set; }

	/// <summary>A click on a row, or Enter on one of its cells.</summary>
	[Parameter]
	public EventCallback<TItem> OnRowClick { get; set; }

	/// <summary>A right-click on a row, which opens its menu.</summary>
	[Parameter]
	public EventCallback<MokaItemContextMenuArgs<TItem>> OnRowMenu { get; set; }

	/// <summary>The buttons at the end of each row, such as its menu button.</summary>
	[Parameter]
	public RenderFragment<TItem>? RowActions { get; set; }

	/// <summary>The width of the column <see cref="RowActions"/> draws in.</summary>
	[Parameter]
	public string ActionsWidth { get; set; } = "auto";

	/// <summary>An extra class per row, such as one that dims a stopped service.</summary>
	[Parameter]
	public Func<TItem, string?>? RowClass { get; set; }

	/// <summary>Groups rows under a header per key, such as containers by compose project.</summary>
	[Parameter]
	public Func<TItem, string>? GroupBy { get; set; }

	/// <summary>What a group's header shows.</summary>
	[Parameter]
	public RenderFragment<MokaTableGroup<TItem>>? GroupHeader { get; set; }

	/// <summary>The groups that start collapsed.</summary>
	[Parameter]
	public IReadOnlyCollection<string>? CollapsedGroups { get; set; }

	/// <summary>A group opened or collapsed by the user.</summary>
	[Parameter]
	public EventCallback<(string Key, bool Collapsed)> GroupToggled { get; set; }

	/// <summary>The column the rows start sorted by, matched against a column's title.</summary>
	[Parameter]
	public string? SortColumn { get; set; }

	/// <summary>The direction of <see cref="SortColumn"/>.</summary>
	[Parameter]
	public MokaSortDirection SortDirection { get; set; } = MokaSortDirection.None;

	/// <summary>The column the user sorted by, for a page that orders rows itself, such as a tree's siblings.</summary>
	[Parameter]
	public EventCallback<string?> SortColumnChanged { get; set; }

	/// <summary>The direction the user sorted in, raised after <see cref="SortColumnChanged"/>.</summary>
	[Parameter]
	public EventCallback<MokaSortDirection> SortDirectionChanged { get; set; }

	/// <summary>Draws placeholder rows while the first read is still under way.</summary>
	[Parameter]
	public bool Loading { get; set; }

	/// <summary>Draws only the rows in view, for lists that run to thousands.</summary>
	[Parameter]
	public bool Virtualize { get; set; }

	/// <summary>The icon of the empty state shown when there are no rows.</summary>
	[Parameter]
	public MokaIconDefinition? EmptyIcon { get; set; }

	/// <summary>The title of the empty state, such as "Nothing matches".</summary>
	[Parameter]
	public string? EmptyTitle { get; set; }

	/// <summary>The sentence under the empty state's title.</summary>
	[Parameter]
	public string? EmptyText { get; set; }

	private HashSet<TItem>? SelectedSet => _selected;

	protected override void OnParametersSet()
	{
		// Rebuilt from the rows handed in each time, because a refresh replaces every item and a set kept from the last
		// read would hold instances the table no longer has.
		_selected = IsSelected is { } isSelected && Items.FirstOrDefault(isSelected) is { } selected ? [selected] : null;
	}
}
