using Microsoft.AspNetCore.Components;
using Moka.Red.Data.Table;
using Moka.Red.Extensions;
using Mokaterm.UI.Common.ToolPages;

namespace Mokaterm.UI.Tests;

/// <summary>
/// The list every tool page draws: Moka's table with its rows, the row whose details are open marked as selected,
/// and the page's own empty state when nothing is left to show.
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
	public async Task NoRows_ShowTheEmptyState()
	{
		string html = await RenderAsync([], isSelected: null);

		Assert.Contains("Nothing matches", html, StringComparison.Ordinal);
		Assert.Contains("Clear the filter.", html, StringComparison.Ordinal);
	}

	private static Task<string> RenderAsync(IReadOnlyList<Unit> units, Func<Unit, bool>? isSelected) =>
		StaticRender.RenderAsync<ToolTable<Unit>>(
			services => services.AddMokaRed(),
			new Dictionary<string, object?>
			{
				[nameof(ToolTable<Unit>.Items)] = units,
				[nameof(ToolTable<Unit>.ItemKey)] = (Func<Unit, object>)(unit => unit.Name),
				[nameof(ToolTable<Unit>.Label)] = "Services",
				[nameof(ToolTable<Unit>.IsSelected)] = isSelected,
				[nameof(ToolTable<Unit>.EmptyTitle)] = "Nothing matches",
				[nameof(ToolTable<Unit>.EmptyText)] = "Clear the filter.",
				[nameof(ToolTable<Unit>.ChildContent)] = (RenderFragment)(builder =>
				{
					builder.OpenComponent<MokaColumn<Unit>>(0);
					builder.AddAttribute(1, nameof(MokaColumn<Unit>.Title), "Service");
					builder.AddAttribute(2, nameof(MokaColumn<Unit>.Field), (Func<Unit, object?>)(unit => unit.Name));
					builder.CloseComponent();
					builder.OpenComponent<MokaColumn<Unit>>(3);
					builder.AddAttribute(4, nameof(MokaColumn<Unit>.Title), "State");
					builder.AddAttribute(5, nameof(MokaColumn<Unit>.Field), (Func<Unit, object?>)(unit => unit.State));
					builder.CloseComponent();
				}),
			})
			.WaitAsync(Wait, TestContext.Current.CancellationToken);

	internal sealed record Unit(string Name, string State);
}
