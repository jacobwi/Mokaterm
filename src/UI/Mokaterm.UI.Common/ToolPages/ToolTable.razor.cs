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
/// <para>
/// With <see cref="BulkActions"/> every row gets a box to pick it and a bar over the header acts on the picked rows. The
/// row whose details are open is then marked through its class rather than as a picked row, so the two never mix: a
/// picked row has its box ticked, the open one does not.
/// </para>
/// </summary>
/// <typeparam name="TItem">What one row shows.</typeparam>
public partial class ToolTable<TItem>
{
	// Every row is exactly this high, menu button or not, which is what lets a virtualized list scroll true whatever the
	// theme's density does to Moka's own cell padding. The style below hands the same number to the CSS.
	private const int RowPixels = 36;

	private static readonly string RowStyle = $"--mt-tool-row: {RowPixels.ToString(System.Globalization.CultureInfo.InvariantCulture)}px";

	// The open row in a table whose rows are picked with boxes: Moka's own selected look, and a class of ours that pages
	// can follow (the Docker manager shows a row's quick actions on it).
	private const string OpenRowClass = "moka-table-row--selected mt-tool-row--open";

	private HashSet<TItem>? _selected;
	private HashSet<TItem> _picked = [];
	private Func<TItem, string?>? _openRowClass;

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

	/// <summary>
	/// What to do with the picked rows, such as Stop or Remove: buttons in a bar over the header while any row is picked.
	/// Setting it puts a box on every row. The bar's context is the picked rows, every one of them in <see cref="Items"/>:
	/// a picked row that leaves the list (removed, or filtered out) is no longer picked.
	/// </summary>
	[Parameter]
	public RenderFragment<IReadOnlyList<TItem>>? BulkActions { get; set; }

	/// <summary>What a row is called, for the names in the bulk bar and its box's label ("Select web-1").</summary>
	[Parameter]
	public Func<TItem, string>? ItemName { get; set; }

	/// <summary>What the rows are, in the plural, such as "containers", for the bulk bar's name.</summary>
	[Parameter]
	public string? ItemsName { get; set; }

	/// <summary>
	/// A button at the end of the header row that lists the columns with a box each, to hide and show them. It sits over
	/// the heading of the <see cref="RowActions"/> column, so use it with row actions, and mark the columns that must
	/// stay (a name, a state dot) <c>Fixed</c>, which also keeps a column without a title out of the list. It steps aside
	/// while rows are picked, when the bulk bar has the header's place.
	/// </summary>
	[Parameter]
	public bool ShowColumnToggle { get; set; }

	/// <summary>The columns someone hid or resized, to put back, such as what <see cref="LayoutChanged"/> gave the last time the page showed.</summary>
	[Parameter]
	public MokaTableLayout? Layout { get; set; }

	/// <summary>Raised when someone hides, shows or resizes a column, so a page can keep the choice while it is away.</summary>
	[Parameter]
	public EventCallback<MokaTableLayout> LayoutChanged { get; set; }

	/// <summary>A muted line under the rows, such as a total: its parts sit apart, the first at the start and the last at the end.</summary>
	[Parameter]
	public RenderFragment? FootContent { get; set; }

	/// <summary>The title of the empty state, such as "Nothing matches".</summary>
	[Parameter]
	public string? EmptyTitle { get; set; }

	/// <summary>The sentence under the empty state's title.</summary>
	[Parameter]
	public string? EmptyText { get; set; }

	private bool IsPickable => BulkActions is not null;

	private HashSet<TItem>? SelectedSet => IsPickable ? _picked : _selected;

	private EventCallback<HashSet<TItem>> PickedChanged => IsPickable
		? EventCallback.Factory.Create<HashSet<TItem>>(this, OnPicked)
		: default;

	private Func<TItem, string?>? RowClassFor => IsSelected is null ? RowClass : _openRowClass ??= OpenRowClassOf;

	private string WrapperClass => (ShowColumnToggle, IsPickable && _picked.Count > 0) switch
	{
		(true, true) => "mt-tool-table mt-tool-table--columns mt-tool-table--picking",
		(true, false) => "mt-tool-table mt-tool-table--columns",
		_ => "mt-tool-table",
	};

	protected override void OnParametersSet()
	{
		// Rebuilt from the rows handed in each time, because a refresh replaces every item and a set kept from the last
		// read would hold instances the table no longer has.
		_selected = !IsPickable && IsSelected is { } isSelected && Items.FirstOrDefault(isSelected) is { } selected ? [selected] : null;
		if (IsPickable)
		{
			CarryPicks();
		}
		else if (_picked.Count > 0)
		{
			_picked = [];
		}
	}

	/// <summary>
	/// Keeps the picks on the rows handed in now: the same row in its new instance after a refresh, and nothing that left
	/// the list, so the bulk bar never acts on a row the user cannot see. A new set only when that changed anything,
	/// because the table takes a new set as a new selection.
	/// </summary>
	private void CarryPicks()
	{
		if (_picked.Count == 0)
		{
			return;
		}

		HashSet<object> keys = [.. _picked.Select(KeyOf)];
		HashSet<TItem> carried = [.. Items.Where(item => keys.Contains(KeyOf(item)))];
		if (!carried.SetEquals(_picked))
		{
			_picked = carried;
		}
	}

	private object KeyOf(TItem item) => ItemKey is { } key ? key(item) : item!;

	private void OnPicked(HashSet<TItem> picked) => _picked = picked;

	private string? OpenRowClassOf(TItem item)
	{
		string? own = RowClass?.Invoke(item);
		if (IsSelected?.Invoke(item) != true)
		{
			return own;
		}

		// Without boxes the open row is the table's selection, which draws Moka's selected look already.
		string open = IsPickable ? OpenRowClass : "mt-tool-row--open";
		return own is null ? open : $"{own} {open}";
	}
}
