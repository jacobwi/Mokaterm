using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Moka.Red.Data.Table;
using Moka.Red.Extensions;
using Mokaterm.UI.Common.Components;

namespace Mokaterm.UI.Tests;

/// <summary>
/// The one sorting column header, shared by the fleet overview and the System view's lists: aria-sort on the header cell
/// of the sorted column only, a real button the keyboard reaches, and the column's description as Moka's tooltip.
/// </summary>
public sealed partial class SortHeaderTests
{
	private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

	[Fact]
	public async Task AColumnTheListDoesNotFollow_SaysNothingAboutSorting()
	{
		string html = await RenderAsync(MokaSortDirection.None);

		Assert.Contains("role=\"columnheader\"", html, StringComparison.Ordinal);
		Assert.DoesNotContain("aria-sort", html, StringComparison.Ordinal);
		Assert.DoesNotContain("mt-sort-header__icon", html, StringComparison.Ordinal);
		Assert.DoesNotContain("mt-sort-header__button--active", html, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(MokaSortDirection.Ascending, "ascending")]
	[InlineData(MokaSortDirection.Descending, "descending")]
	public async Task TheSortedColumn_CarriesAriaSortOnItsHeaderCell_NotOnItsButton(MokaSortDirection direction, string expected)
	{
		string html = await RenderAsync(direction);

		Match cell = CellTag().Match(html);
		Assert.True(cell.Success, "No column header.");
		Assert.Contains($"aria-sort=\"{expected}\"", cell.Value, StringComparison.Ordinal);
		Assert.Single(AriaSort().Matches(html));
		Assert.DoesNotContain("aria-sort", ButtonTag().Match(html).Value, StringComparison.Ordinal);
		Assert.Contains("mt-sort-header__button--active", html, StringComparison.Ordinal);
		Assert.Contains("mt-sort-header__icon", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task TheArrow_PointsTheWayTheColumnRuns()
	{
		string ascending = Icon(await RenderAsync(MokaSortDirection.Ascending));
		string descending = Icon(await RenderAsync(MokaSortDirection.Descending));

		Assert.NotEqual(ascending, descending);
	}

	[Fact]
	public async Task TheKeyboard_ReachesARealButton_InsideTheHeaderCell()
	{
		// Static rendering presses no keys; a button in the tab order is what makes Enter and Space press it, and its click
		// comes up to the cell, which sorts.
		string html = await RenderAsync(MokaSortDirection.None);

		string button = ButtonTag().Match(html).Value;
		Assert.StartsWith("<button type=\"button\"", button, StringComparison.Ordinal);
		Assert.DoesNotContain("tabindex", button, StringComparison.Ordinal);
		Assert.DoesNotContain("disabled", button, StringComparison.Ordinal);
		int cell = html.IndexOf("role=\"columnheader\"", StringComparison.Ordinal);
		int at = html.IndexOf("<button", StringComparison.Ordinal);
		Assert.True(cell >= 0 && cell < at && at < html.LastIndexOf("</span>", StringComparison.Ordinal), "The button is not inside the header cell.");
		Assert.Matches("<span class=\"mt-sort-header__label\"[^>]*>Load</span>", html);
	}

	[Fact]
	public async Task ADescription_IsTheHeadersTooltip_AroundItsButton()
	{
		string html = await RenderAsync(MokaSortDirection.None, description: "Load average over one minute");

		Assert.Equal(["Load average over one minute"], Tooltips.Texts(html));
		Assert.Contains("<button type=\"button\"", Tooltips.ControlFor(html, "Load average over one minute"), StringComparison.Ordinal);
	}

	[Fact]
	public async Task WithoutADescription_ThereIsNoTooltip()
	{
		string html = await RenderAsync(MokaSortDirection.None);

		Assert.Empty(Tooltips.Texts(html));
		Assert.DoesNotContain("role=\"tooltip\"", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task TheCell_KeepsTheCallersClasses()
	{
		string html = await RenderAsync(MokaSortDirection.None, cssClass: "mta-wide");

		Assert.Contains("class=\"mt-sort-header mta-wide\"", CellTag().Match(html).Value, StringComparison.Ordinal);
	}

	[Fact]
	public async Task InARealTable_ItIsATh_WhichIsAColumnHeaderAlready()
	{
		string html = await RenderAsync(MokaSortDirection.Descending, tableCell: true);

		Assert.Matches("^<th class=\"mt-sort-header\" aria-sort=\"descending\"[^>]*>", html.TrimStart());
		Assert.DoesNotContain("role=\"columnheader\"", html, StringComparison.Ordinal);
		Assert.Single(AriaSort().Matches(html));
		Assert.Contains("<button type=\"button\"", html, StringComparison.Ordinal);
		Assert.EndsWith("</th>", html.TrimEnd(), StringComparison.Ordinal);
	}

	private static string Icon(string html)
	{
		int start = html.IndexOf("mt-sort-header__icon", StringComparison.Ordinal);
		Assert.True(start >= 0, "No arrow.");
		int open = html.LastIndexOf('<', start);
		return html[open..html.IndexOf("</button>", start, StringComparison.Ordinal)];
	}

	private static Task<string> RenderAsync(MokaSortDirection direction, string? description = null, string? cssClass = null, bool tableCell = false) =>
		StaticRender.RenderAsync<SortHeader>(
			services => services.AddMokaRed(),
			new Dictionary<string, object?>
			{
				[nameof(SortHeader.Label)] = "Load",
				[nameof(SortHeader.Direction)] = direction,
				[nameof(SortHeader.Description)] = description,
				[nameof(SortHeader.Class)] = cssClass,
				[nameof(SortHeader.TableCell)] = tableCell,
				[nameof(SortHeader.OnSort)] = EventCallback.Empty,
			})
			.WaitAsync(Wait, TestContext.Current.CancellationToken);

	[GeneratedRegex("<span[^>]*role=\"columnheader\"[^>]*>")]
	private static partial Regex CellTag();

	[GeneratedRegex("<button[^>]*>")]
	private static partial Regex ButtonTag();

	[GeneratedRegex("aria-sort=")]
	private static partial Regex AriaSort();
}
