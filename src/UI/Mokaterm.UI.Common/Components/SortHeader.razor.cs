using Microsoft.AspNetCore.Components;
using Moka.Red.Data.Table;

namespace Mokaterm.UI.Common.Components;

/// <summary>
/// A column header that sorts its list when clicked, for the lists that are not a <c>MokaTable</c>: the fleet overview,
/// the System view's pages and the Docker stats table. It only draws which way its column runs and reports the click;
/// the order, its cycle and the direction a first click picks stay with the caller, because a list of figures and a
/// list of names start differently and not every list goes back to its own order on a third click.
/// </summary>
/// <remarks>
/// The header is a <c>role="columnheader"</c> span, so it belongs in a <c>role="row"</c> of a grid or table for
/// <c>aria-sort</c> to be read out; with <see cref="TableCell"/> it is a <c>th</c> for the header row of a real
/// <c>table</c>. The cell takes the caller's classes, which place it in the row's column template.
/// </remarks>
public sealed partial class SortHeader : ComponentBase
{
	/// <summary>Draws the header as a <c>th</c>, for a <c>table</c> whose rows can hold only table cells.</summary>
	[Parameter]
	public bool TableCell { get; set; }

	[Parameter, EditorRequired]
	public string Label { get; set; } = "";

	/// <summary>What the column holds, shown as a tooltip on hover and keyboard focus. Null for a plain header.</summary>
	[Parameter]
	public string? Description { get; set; }

	/// <summary>Which way this column runs, or None while the list follows another column or none.</summary>
	[Parameter]
	public MokaSortDirection Direction { get; set; }

	[Parameter]
	public EventCallback OnSort { get; set; }

	/// <summary>Classes for the header cell, such as the ones that place it in a grid row or hide it on a narrow one.</summary>
	[Parameter]
	public string? Class { get; set; }

	private string CssClass => string.IsNullOrWhiteSpace(Class) ? "mt-sort-header" : "mt-sort-header " + Class;

	private string? AriaSort => Direction switch
	{
		MokaSortDirection.Ascending => "ascending",
		MokaSortDirection.Descending => "descending",
		_ => null,
	};

	private string ButtonClass => Direction == MokaSortDirection.None
		? "mt-sort-header__button"
		: "mt-sort-header__button mt-sort-header__button--active";
}
