using Microsoft.AspNetCore.Components;
using Moka.Red.Data.Table;
using Moka.Red.Extensions;
using Mokaterm.UI.Common.ToolPages;

namespace Mokaterm.UI.Tests;

/// <summary>
/// The list every tool page draws: Moka's table with its rows, the row whose details are open marked as selected,
/// and the page's own empty state when nothing is left to show. Boxes to pick rows, the column toggle and the foot are
/// there only for a page that asks, so the System view's lists stay as they were.
/// </summary>
public sealed class ToolTableTests
{
	private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

	private static readonly Unit[] Units = [new("cron.service", "running"), new("ssh.service", "running"), new("backup.service", "failed")];

	[Fact]
	public async Task Rows_AreDrawnInAMokaTable_WithTheOpenRowSelected()
	{
		string html = await RenderAsync(Units, unit => unit.Name == "ssh.service");

		Assert.Contains("mt-tool-table", html, StringComparison.Ordinal);
		Assert.Contains("<table", html, StringComparison.Ordinal);
		Assert.Contains("aria-label=\"Services\"", html, StringComparison.Ordinal);
		foreach (Unit unit in Units)
		{
			Assert.Contains(unit.Name, html, StringComparison.Ordinal);
		}

		Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "moka-table-row--selected"));
		int selected = html.IndexOf("moka-table-row--selected", StringComparison.Ordinal);
		int row = html.IndexOf("ssh.service", StringComparison.Ordinal);
		Assert.True(selected < row && html.IndexOf("</tr>", selected, StringComparison.Ordinal) > row, "The selected row is not the open one.");
	}

	[Fact]
	public async Task NoOpenRow_SelectsNothing()
	{
		string html = await RenderAsync(Units, isSelected: null);

		Assert.DoesNotContain("moka-table-row--selected", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task WithoutBulkActions_RowsHaveNoBoxes_AndThereIsNoToolbar()
	{
		string html = await RenderAsync(Units, unit => unit.Name == "ssh.service");

		Assert.DoesNotContain("moka-table-cell--checkbox", html, StringComparison.Ordinal);
		Assert.DoesNotContain("moka-table-toolbar", html, StringComparison.Ordinal);
		Assert.DoesNotContain("moka-table-bulk", html, StringComparison.Ordinal);
		Assert.DoesNotContain("moka-table-foot-content", html, StringComparison.Ordinal);
		Assert.Contains("class=\"mt-tool-table\"", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task WithBulkActions_EveryRowHasABox_AndTheOpenRowIsMarkedByItsClassNotPicked()
	{
		string html = await RenderAsync(
			Units,
			unit => unit.Name == "ssh.service",
			new Dictionary<string, object?>
			{
				[nameof(ToolTable<Unit>.BulkActions)] = (RenderFragment<IReadOnlyList<Unit>>)(picked => builder => builder.AddContent(0, "Stop them")),
				[nameof(ToolTable<Unit>.ItemName)] = (Func<Unit, string>)(unit => unit.Name),
				[nameof(ToolTable<Unit>.ItemsName)] = "services",
			});

		Assert.Contains("aria-label=\"Select ssh.service\"", html, StringComparison.Ordinal);
		Assert.Equal(Units.Length, System.Text.RegularExpressions.Regex.Count(html, "<td class=\"moka-table-cell--checkbox\""));
		Assert.DoesNotContain("checked", html, StringComparison.Ordinal);

		// Nothing is picked, so there is no bar yet; the open row still looks selected, through its own class.
		Assert.DoesNotContain("Stop them", html, StringComparison.Ordinal);
		Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "mt-tool-row--open"));
		int open = html.IndexOf("mt-tool-row--open", StringComparison.Ordinal);
		int row = html.IndexOf("ssh.service", open, StringComparison.Ordinal);
		Assert.True(html.LastIndexOf("moka-table-row--selected", open, StringComparison.Ordinal) > html.LastIndexOf("<tr", open, StringComparison.Ordinal), "The open row has Moka's selected look.");
		Assert.True(row > open && html.IndexOf("</tr>", open, StringComparison.Ordinal) > row, "The marked row is not the open one.");
	}

	[Fact]
	public async Task TheColumnToggle_IsMokasOwn_InATableMarkedForIt()
	{
		string html = await RenderAsync(
			Units,
			isSelected: null,
			new Dictionary<string, object?> { [nameof(ToolTable<Unit>.ShowColumnToggle)] = true },
			fixedFirst: true);

		Assert.Contains("mt-tool-table mt-tool-table--columns", html, StringComparison.Ordinal);
		Assert.Contains("aria-label=\"Toggle columns\"", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task TheFoot_IsALineUnderTheRows()
	{
		string html = await RenderAsync(
			Units,
			isSelected: null,
			new Dictionary<string, object?> { [nameof(ToolTable<Unit>.FootContent)] = (RenderFragment)(builder => builder.AddContent(0, "3 services")) });

		int foot = html.IndexOf("moka-table-foot-content", StringComparison.Ordinal);
		Assert.True(foot > html.LastIndexOf("backup.service", StringComparison.Ordinal), "The foot comes after the rows.");
		Assert.Contains("3 services", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task NoRows_ShowTheEmptyState()
	{
		string html = await RenderAsync([], isSelected: null);

		Assert.Contains("Nothing matches", html, StringComparison.Ordinal);
		Assert.Contains("Clear the filter.", html, StringComparison.Ordinal);
	}

	private static Task<string> RenderAsync(
		IReadOnlyList<Unit> units,
		Func<Unit, bool>? isSelected,
		Dictionary<string, object?>? extra = null,
		bool fixedFirst = false)
	{
		Dictionary<string, object?> parameters = new()
		{
			[nameof(ToolTable<Unit>.Items)] = units,
			[nameof(ToolTable<Unit>.ItemKey)] = (Func<Unit, object>)(unit => unit.Name),
			[nameof(ToolTable<Unit>.Label)] = "Services",
			[nameof(ToolTable<Unit>.IsSelected)] = isSelected,
			[nameof(ToolTable<Unit>.EmptyTitle)] = "Nothing matches",
			[nameof(ToolTable<Unit>.EmptyText)] = "Clear the filter.",
			[nameof(ToolTable<Unit>.RowActions)] = (RenderFragment<Unit>)(unit => builder => builder.AddContent(0, "...")),
			[nameof(ToolTable<Unit>.ChildContent)] = (RenderFragment)(builder =>
			{
				builder.OpenComponent<MokaColumn<Unit>>(0);
				builder.AddAttribute(1, nameof(MokaColumn<Unit>.Title), "Service");
				builder.AddAttribute(2, nameof(MokaColumn<Unit>.Field), (Func<Unit, object?>)(unit => unit.Name));
				builder.AddAttribute(3, nameof(MokaColumn<Unit>.Fixed), fixedFirst);
				builder.CloseComponent();
				builder.OpenComponent<MokaColumn<Unit>>(4);
				builder.AddAttribute(5, nameof(MokaColumn<Unit>.Title), "State");
				builder.AddAttribute(6, nameof(MokaColumn<Unit>.Field), (Func<Unit, object?>)(unit => unit.State));
				builder.CloseComponent();
			}),
		};

		foreach ((string name, object? value) in extra ?? [])
		{
			parameters[name] = value;
		}

		return StaticRender.RenderAsync<ToolTable<Unit>>(services => services.AddMokaRed(), parameters)
			.WaitAsync(Wait, TestContext.Current.CancellationToken);
	}

	internal sealed record Unit(string Name, string State);
}
