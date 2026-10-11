using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Moka.Red.Extensions;
using Mokaterm.Tests.Shared;
using Mokaterm.UI.Workspace;

namespace Mokaterm.UI.Tests;

/// <summary>
/// The layout drawn for real: a splitter per split, outermost first, and each pane's content where the tree puts it.
/// </summary>
public sealed partial class PaneLayoutViewTests
{
	private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task OnePane_IsDrawnWithNoSplitter()
	{
		string html = await RenderAsync(new PaneLeaf(7));

		Assert.DoesNotContain("moka-split-pane", html, StringComparison.Ordinal);
		Assert.Equal(["7"], Panes(html));
	}

	[Fact]
	public async Task SideBySide_IsOneSplitterBetweenTwoPanes()
	{
		string html = await RenderAsync(PaneLayout.Build(PaneArrangement.SideBySide, [4, 9], Counter()));

		Assert.Equal(["horizontal"], Orientations(html));
		Assert.Equal(["4", "9"], Panes(html));
		Assert.Contains("mt-pane-split", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task OneLargeAndTwoStacked_StacksTheTwoBesideTheFirst()
	{
		string html = await RenderAsync(PaneLayout.Build(PaneArrangement.MainAndStack, [10, 11, 12], Counter()));

		Assert.Equal(["horizontal", "vertical"], Orientations(html));
		Assert.Equal(["10", "11", "12"], Panes(html));

		// The first pane sits in the outer split's left half, ahead of the stack.
		Assert.True(html.IndexOf(">10<", StringComparison.Ordinal) < html.IndexOf("moka-split-pane--vertical", StringComparison.Ordinal));
	}

	[Fact]
	public async Task Grid_IsTwoRowsOfTwoInReadingOrder()
	{
		string html = await RenderAsync(PaneLayout.Build(PaneArrangement.Grid, [10, 11, 12, 13], Counter()));

		Assert.Equal(["vertical", "horizontal", "horizontal"], Orientations(html));
		Assert.Equal(["10", "11", "12", "13"], Panes(html));
	}

	private static Func<int> Counter()
	{
		int next = 100;
		return () => next++;
	}

	private static List<string> Panes(string html) => [.. PaneContent().Matches(html).Select(match => match.Groups[1].Value)];

	private static List<string> Orientations(string html) => [.. Orientation().Matches(html).Select(match => match.Groups[1].Value)];

	private static Task<string> RenderAsync(PaneNode layout)
	{
		RenderFragment<int> pane = id => builder =>
		{
			builder.OpenElement(0, "i");
			builder.AddAttribute(1, "class", "test-pane");
			builder.AddContent(2, id.ToString(CultureInfo.InvariantCulture));
			builder.CloseElement();
		};

		return StaticRender.RenderAsync<PaneLayoutView>(
			services => services.AddMokaRed(),
			new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				[nameof(PaneLayoutView.Layout)] = layout,
				[nameof(PaneLayoutView.Pane)] = pane,
			}).WaitAsync(Patience, Ct);
	}

	[GeneratedRegex("<i class=\"test-pane\">([0-9]+)</i>")]
	private static partial Regex PaneContent();

	[GeneratedRegex("moka-split-pane--(horizontal|vertical)")]
	private static partial Regex Orientation();
}
