using System.Globalization;
using Mokaterm.UI.Workspace;

namespace Mokaterm.UI.Tests;

/// <summary>
/// The pane tree on its own: the named layouts, reading order, splitting and pruning, and the shape the plain
/// <c>ui-state</c> document keeps, which a hand-edited file must not be able to break.
/// </summary>
public sealed class PaneLayoutTests
{
	[Theory]
	[InlineData(nameof(PaneArrangement.Single), "P")]
	[InlineData(nameof(PaneArrangement.SideBySide), "H(P,P)")]
	[InlineData(nameof(PaneArrangement.Stacked), "V(P,P)")]
	[InlineData(nameof(PaneArrangement.MainAndStack), "H(P,V(P,P))")]
	[InlineData(nameof(PaneArrangement.Grid), "V(H(P,P),H(P,P))")]
	public void Build_EveryNamedLayout_HasItsShapeAndNumbersThePanesInReadingOrder(string name, string shape)
	{
		PaneArrangement arrangement = Enum.Parse<PaneArrangement>(name);
		int[] ids = [10, 11, 12, 13];
		int splits = 100;

		PaneNode layout = PaneLayout.Build(arrangement, ids, () => splits++);

		Assert.Equal(shape, PaneLayout.Write(layout));
		Assert.Equal(ids.Take(PaneLayout.PaneCount(arrangement)), PaneLayout.Panes(layout));
	}

	[Theory]
	[InlineData("P")]
	[InlineData("H(P,P)")]
	[InlineData("V(P,P)")]
	[InlineData("H(P,V(P,P))")]
	[InlineData("V(H(P,P),H(P,P))")]
	[InlineData("H(P,H(P,P))")]
	[InlineData("V(H(P,P),P)")]
	public void Read_WhatWriteWrote_ComesBackWithFreshIdsInPaneOrder(string shape)
	{
		int panes = 0;
		int splits = 100;

		PaneNode? layout = PaneLayout.Read(shape, 4, () => panes++, () => splits++);

		Assert.NotNull(layout);
		Assert.Equal(shape, PaneLayout.Write(layout));
		Assert.Equal(Enumerable.Range(0, panes), PaneLayout.Panes(layout));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("X")]
	[InlineData("p")]
	[InlineData("PP")]
	[InlineData("H(P)")]
	[InlineData("H(P,P")]
	[InlineData("H(P,P))")]
	[InlineData("H(P, P)")]
	[InlineData("h(P,P)")]
	[InlineData("H(P,H(P,H(P,H(P,P))))")]
	[InlineData("V(H(P,P),H(P,H(P,P)))")]
	[InlineData("H(H(H(H(H(H(H(H(H(H(H(P,P),P),P),P),P),P),P),P),P),P),P)")]
	public void Read_AnythingElse_IsNull(string? code) =>
		Assert.Null(PaneLayout.Read(code, 4, () => 0, () => 0));

	[Fact]
	public void Prune_ASplitThatLostAHalf_GivesWayToTheOtherAndLeavesTheRestAlone()
	{
		PaneNode grid = PaneLayout.Build(PaneArrangement.Grid, [0, 1, 2, 3], Counter(100));
		PaneSplit bottom = (PaneSplit)((PaneSplit)grid).Second;

		PaneNode? pruned = PaneLayout.Prune(grid, pane => pane != 1);

		PaneSplit root = Assert.IsType<PaneSplit>(pruned);
		Assert.Equal("V(P,H(P,P))", PaneLayout.Write(root));
		Assert.Equal(((PaneSplit)grid).Id, root.Id);
		Assert.Same(bottom, root.Second);
	}

	[Fact]
	public void Prune_EveryPaneKept_IsTheSameTree()
	{
		PaneNode grid = PaneLayout.Build(PaneArrangement.Grid, [0, 1, 2, 3], Counter(100));

		Assert.Same(grid, PaneLayout.Prune(grid, _ => true));
	}

	[Fact]
	public void Prune_NoPaneKept_IsNull()
	{
		PaneNode grid = PaneLayout.Build(PaneArrangement.Grid, [0, 1, 2, 3], Counter(100));

		Assert.Null(PaneLayout.Prune(grid, _ => false));
	}

	[Theory]
	[InlineData(nameof(PaneDirection.Right), "H(P,V(H(P,P),P))", "0,1,9,2")]
	[InlineData(nameof(PaneDirection.Left), "H(P,V(H(P,P),P))", "0,9,1,2")]
	[InlineData(nameof(PaneDirection.Down), "H(P,V(V(P,P),P))", "0,1,9,2")]
	[InlineData(nameof(PaneDirection.Up), "H(P,V(V(P,P),P))", "0,9,1,2")]
	public void Split_OpensTheNewPaneOnThatSideOfThatPaneOnly(string direction, string shape, string order)
	{
		PaneSplit three = (PaneSplit)PaneLayout.Build(PaneArrangement.MainAndStack, [0, 1, 2], Counter(100));

		PaneSplit split = Assert.IsType<PaneSplit>(PaneLayout.Split(three, 1, Enum.Parse<PaneDirection>(direction), 9, 50));

		Assert.Equal(shape, PaneLayout.Write(split));
		Assert.Equal(order.Split(',').Select(pane => int.Parse(pane, CultureInfo.InvariantCulture)), PaneLayout.Panes(split));
		Assert.Equal(three.Id, split.Id);
		Assert.Same(three.First, split.First);
	}

	[Fact]
	public void Neighbour_IsThePaneThatTakesTheSpace()
	{
		PaneNode grid = PaneLayout.Build(PaneArrangement.Grid, [0, 1, 2, 3], Counter(100));
		PaneNode three = PaneLayout.Build(PaneArrangement.MainAndStack, [0, 1, 2], Counter(100));

		Assert.Equal(1, PaneLayout.Neighbour(grid, 0));
		Assert.Equal(0, PaneLayout.Neighbour(grid, 1));
		Assert.Equal(3, PaneLayout.Neighbour(grid, 2));
		Assert.Equal(1, PaneLayout.Neighbour(three, 0));
		Assert.Equal(2, PaneLayout.Neighbour(three, 1));
		Assert.Equal(1, PaneLayout.Neighbour(three, 2));
		Assert.Null(PaneLayout.Neighbour(new PaneLeaf(0), 0));
	}

	[Theory]
	[InlineData(1, nameof(PaneArrangement.Single))]
	[InlineData(2, nameof(PaneArrangement.SideBySide))]
	[InlineData(3, nameof(PaneArrangement.MainAndStack))]
	[InlineData(4, nameof(PaneArrangement.Grid))]
	public void ForCount_IsTheNamedLayoutAPaneMoreGrowsInto(int panes, string name)
	{
		PaneArrangement arrangement = PaneLayout.ForCount(panes);

		Assert.Equal(Enum.Parse<PaneArrangement>(name), arrangement);
		Assert.Equal(panes, PaneLayout.PaneCount(arrangement));
	}

	private static Func<int> Counter(int start) => () => start++;
}
