using Mokaterm.Abstractions.Sessions;
using Mokaterm.UI.Tests.Fakes;
using Mokaterm.UI.Workspace;

namespace Mokaterm.UI.Tests;

/// <summary>
/// The pane model with no renderer: where a tab goes, how panes open and give way, which pane has the focus, the named
/// layouts and the layout a restore lays out before its tabs arrive.
/// </summary>
public sealed class SessionWorkspaceTests
{
	[Fact]
	public void NewSessions_JoinThePaneTheUserIsWorkingIn()
	{
		ManualSessionManager manager = new();
		using SessionWorkspace workspace = new(manager);
		manager.Add();
		FakeSession right = manager.Add();
		workspace.MoveToOtherPane(right.Id);

		FakeSession third = manager.Add();

		Assert.Equal(workspace.PaneOf(right.Id), workspace.PaneOf(third.Id));
		Assert.Equal(1, workspace.PositionOf(workspace.PaneOf(third.Id)));
		Assert.Equal(workspace.PaneOf(third.Id), workspace.FocusedPane);
	}

	[Fact]
	public void MoveToOtherPane_SplitsAndFollowsTheTab()
	{
		ManualSessionManager manager = new();
		using SessionWorkspace workspace = new(manager);
		FakeSession stays = manager.Add();
		FakeSession moves = manager.Add();

		workspace.MoveToOtherPane(moves.Id);

		Assert.True(workspace.IsSplit);
		Assert.Equal("H(P,P)", PaneLayout.Write(workspace.Layout));
		Assert.Equal([stays.Id], TabsAt(workspace, 0));
		Assert.Equal([moves.Id], TabsAt(workspace, 1));
		Assert.Equal(workspace.Panes[1], workspace.FocusedPane);
		Assert.Equal(moves.Id, workspace.ActiveId);
		Assert.Equal(stays.Id, workspace.ActiveIdIn(workspace.Panes[0]));
	}

	// The tab would leave an empty pane behind only to fill a new one in the same place.
	[Fact]
	public void MoveToOtherPane_TheOnlyTab_StaysWhereItIs()
	{
		ManualSessionManager manager = new();
		using SessionWorkspace workspace = new(manager);
		FakeSession only = manager.Add();
		PaneNode before = workspace.Layout;

		workspace.MoveToOtherPane(only.Id);

		Assert.Same(before, workspace.Layout);
		Assert.Equal(workspace.FocusedPane, workspace.PaneOf(only.Id));
	}

	[Fact]
	public void MoveToOtherPane_OnTheLastTabThere_EndsTheSplit()
	{
		ManualSessionManager manager = new();
		using SessionWorkspace workspace = new(manager);
		manager.Add();
		FakeSession moved = manager.Add();
		int first = workspace.PaneOf(moved.Id);
		workspace.MoveToOtherPane(moved.Id);

		workspace.MoveToOtherPane(moved.Id);

		Assert.False(workspace.IsSplit);
		Assert.Equal(first, workspace.PaneOf(moved.Id));
		Assert.Equal(first, workspace.FocusedPane);
		Assert.IsType<PaneLeaf>(workspace.Layout);
	}

	[Fact]
	public void MoveToOtherPane_WithThreePanes_GoesOnToTheNextAndWrapsAround()
	{
		ManualSessionManager manager = new();
		using SessionWorkspace workspace = new(manager);
		Open(manager, 5);
		workspace.Arrange(PaneArrangement.MainAndStack);
		Guid last = Assert.Single(TabsAt(workspace, 2));
		workspace.FocusPaneAt(2);
		FakeSession extra = manager.Add();
		Assert.Equal(2, workspace.PositionOf(workspace.PaneOf(extra.Id)));

		workspace.MoveToOtherPane(extra.Id);

		Assert.Equal(0, workspace.PositionOf(workspace.PaneOf(extra.Id)));
		Assert.Equal(3, workspace.PaneCount);
		Assert.Equal([last], TabsAt(workspace, 2));
	}

	[Fact]
	public void ClosingTheLastTabOfAPane_EndsTheSplitAndMovesTheFocus()
	{
		ManualSessionManager manager = new();
		using SessionWorkspace workspace = new(manager);
		FakeSession left = manager.Add();
		FakeSession right = manager.Add();
		workspace.MoveToOtherPane(right.Id);
		int rightPane = workspace.PaneOf(right.Id);

		manager.Remove(right.Id);

		Assert.False(workspace.IsSplit);
		Assert.Equal(workspace.PaneOf(left.Id), workspace.FocusedPane);
		Assert.Equal(left.Id, workspace.ActiveId);
		Assert.Null(workspace.ActiveIdIn(rightPane));
	}

	[Fact]
	public void ClosingTheActiveTab_ActivatesItsNeighbourInTheSamePane()
	{
		ManualSessionManager manager = new();
		using SessionWorkspace workspace = new(manager);
		FakeSession first = manager.Add();
		FakeSession second = manager.Add();
		FakeSession third = manager.Add();
		workspace.MoveToOtherPane(third.Id);
		workspace.FocusPane(workspace.PaneOf(first.Id));
		workspace.Activate(second.Id);

		manager.Remove(second.Id);

		Assert.Equal(first.Id, workspace.ActiveIdIn(workspace.PaneOf(first.Id)));
		Assert.Equal(third.Id, workspace.ActiveIdIn(workspace.PaneOf(third.Id)));
	}

	[Fact]
	public void Move_ReordersInsideOnePaneAndLeavesTheOtherAlone()
	{
		ManualSessionManager manager = new();
		using SessionWorkspace workspace = new(manager);
		FakeSession first = manager.Add();
		FakeSession second = manager.Add();
		FakeSession third = manager.Add();
		FakeSession other = manager.Add();
		workspace.MoveToOtherPane(other.Id);

		workspace.Move(third.Id, 0);

		Assert.Equal([third.Id, first.Id, second.Id], TabsAt(workspace, 0));
		Assert.Equal([other.Id], TabsAt(workspace, 1));
	}

	[Fact]
	public void ActivateRelative_StaysInTheFocusedPane()
	{
		ManualSessionManager manager = new();
		using SessionWorkspace workspace = new(manager);
		FakeSession first = manager.Add();
		FakeSession second = manager.Add();
		FakeSession right = manager.Add();
		workspace.MoveToOtherPane(right.Id);
		workspace.FocusPane(workspace.PaneOf(first.Id));
		workspace.Activate(first.Id);

		workspace.ActivateRelative(1);

		Assert.Equal(second.Id, workspace.ActiveId);

		workspace.ActivateRelative(1);

		// Two tabs in this pane, so it wraps instead of stepping into the other one.
		Assert.Equal(first.Id, workspace.ActiveId);
	}

	[Fact]
	public void Activate_FollowsTheTabIntoItsPane()
	{
		ManualSessionManager manager = new();
		using SessionWorkspace workspace = new(manager);
		FakeSession left = manager.Add();
		FakeSession right = manager.Add();
		workspace.MoveToOtherPane(right.Id);
		workspace.FocusPane(workspace.PaneOf(left.Id));

		workspace.Activate(right.Id);

		Assert.Equal(workspace.PaneOf(right.Id), workspace.FocusedPane);
		Assert.Equal(right.Id, workspace.ActiveId);
		Assert.Equal(left.Id, workspace.ActiveIdIn(workspace.PaneOf(left.Id)));
	}

	[Fact]
	public void OpenOrder_KeepsEveryTabOfEveryPane()
	{
		ManualSessionManager manager = new();
		using SessionWorkspace workspace = new(manager);
		FakeSession[] tabs = Open(manager, 4);
		workspace.Arrange(PaneArrangement.Grid);

		Assert.Equal(tabs.Select(tab => tab.Id), workspace.OpenOrder.Select(session => session.Id));
		Assert.Equal(4, workspace.Tabs.Count);
	}

	[Fact]
	public void MoveToPane_PastTheLastPane_GrowsThroughTheNamedLayouts()
	{
		ManualSessionManager manager = new();
		using SessionWorkspace workspace = new(manager);
		FakeSession[] tabs = Open(manager, 4);

		Assert.True(workspace.MoveToPane(tabs[1].Id, 1));
		Assert.Equal("H(P,P)", PaneLayout.Write(workspace.Layout));

		Assert.True(workspace.MoveToPane(tabs[2].Id, 2));
		Assert.Equal("H(P,V(P,P))", PaneLayout.Write(workspace.Layout));

		Assert.True(workspace.MoveToPane(tabs[3].Id, 3));
		Assert.Equal("V(H(P,P),H(P,P))", PaneLayout.Write(workspace.Layout));

		for (int position = 0; position < 4; position++)
		{
			Assert.Equal([tabs[position].Id], TabsAt(workspace, position));
		}

		Assert.Equal(workspace.Panes[3], workspace.FocusedPane);
	}

	// Splitting the second of two side by side is already the three pane layout, so the first split stays as it was.
	[Fact]
	public void MoveToPane_ThirdOfTwoSideBySide_KeepsTheSplitThatWasThere()
	{
		ManualSessionManager manager = new();
		using SessionWorkspace workspace = new(manager);
		FakeSession[] tabs = Open(manager, 3);
		workspace.MoveToPane(tabs[1].Id, 1);
		workspace.MoveToPane(tabs[2].Id, 1);
		PaneSplit before = Assert.IsType<PaneSplit>(workspace.Layout);

		workspace.MoveToPane(tabs[2].Id, 2);

		PaneSplit after = Assert.IsType<PaneSplit>(workspace.Layout);
		Assert.Equal(before.Id, after.Id);
		Assert.Same(before.First, after.First);
	}

	[Fact]
	public void MoveToPane_WithFourPanes_HasNoFifth()
	{
		ManualSessionManager manager = new();
		using SessionWorkspace workspace = new(manager);
		FakeSession[] tabs = Open(manager, 5);
		workspace.Arrange(PaneArrangement.Grid);
		PaneNode before = workspace.Layout;

		Assert.False(workspace.MoveToPane(tabs[0].Id, 4));
		Assert.Same(before, workspace.Layout);
	}

	[Fact]
	public void MoveToPane_TheOnlyTabOfItsPane_OpensNoOther()
	{
		ManualSessionManager manager = new();
		using SessionWorkspace workspace = new(manager);
		FakeSession[] tabs = Open(manager, 2);
		workspace.MoveToOtherPane(tabs[1].Id);

		Assert.False(workspace.MoveToPane(tabs[1].Id, 2));
		Assert.Equal(2, workspace.PaneCount);
	}

	[Fact]
	public void MoveToPane_AnotherPane_TakesTheTabAndItsFocus()
	{
		ManualSessionManager manager = new();
		using SessionWorkspace workspace = new(manager);
		FakeSession[] tabs = Open(manager, 4);
		workspace.Arrange(PaneArrangement.MainAndStack);
		FakeSession moving = manager.Add();

		Assert.True(workspace.MoveToPane(moving.Id, 1));

		Assert.Equal(1, workspace.PositionOf(workspace.PaneOf(moving.Id)));
		Assert.Equal(moving.Id, workspace.ActiveId);
		Assert.Equal(tabs.Length + 1, workspace.Tabs.Count);
	}

	[Theory]
	[InlineData(nameof(PaneDirection.Right), "H(P,P)", 1)]
	[InlineData(nameof(PaneDirection.Left), "H(P,P)", 0)]
	[InlineData(nameof(PaneDirection.Down), "V(P,P)", 1)]
	[InlineData(nameof(PaneDirection.Up), "V(P,P)", 0)]
	public void MoveToNewPane_PutsTheTabOnThatSide(string direction, string layout, int position)
	{
		ManualSessionManager manager = new();
		using SessionWorkspace workspace = new(manager);
		FakeSession stays = manager.Add();
		FakeSession moves = manager.Add();

		Assert.True(workspace.MoveToNewPane(moves.Id, Enum.Parse<PaneDirection>(direction)));

		Assert.Equal(layout, PaneLayout.Write(workspace.Layout));
		Assert.Equal([moves.Id], TabsAt(workspace, position));
		Assert.Equal([stays.Id], TabsAt(workspace, 1 - position));
		Assert.Equal(workspace.PaneOf(moves.Id), workspace.FocusedPane);
	}

	[Fact]
	public void MoveToNewPane_SplitsOnlyTheTabsOwnPane()
	{
		ManualSessionManager manager = new();
		using SessionWorkspace workspace = new(manager);
		FakeSession[] tabs = Open(manager, 3);
		workspace.MoveToOtherPane(tabs[2].Id);
		workspace.MoveToPane(tabs[1].Id, 1);
		PaneSplit before = Assert.IsType<PaneSplit>(workspace.Layout);

		Assert.True(workspace.MoveToNewPane(tabs[2].Id, PaneDirection.Down));

		PaneSplit after = Assert.IsType<PaneSplit>(workspace.Layout);
		Assert.Equal("H(P,V(P,P))", PaneLayout.Write(after));
		Assert.Equal(before.Id, after.Id);
		Assert.Same(before.First, after.First);
		Assert.Equal([tabs[2].Id], TabsAt(workspace, 2));
	}

	[Fact]
	public void MoveToNewPane_WithFourPanes_IsRefused()
	{
		ManualSessionManager manager = new();
		using SessionWorkspace workspace = new(manager);
		FakeSession[] tabs = Open(manager, 5);
		workspace.Arrange(PaneArrangement.Grid);

		Assert.False(workspace.MoveToNewPane(tabs[0].Id, PaneDirection.Right));
		Assert.Equal(4, workspace.PaneCount);
	}

	[Fact]
	public void ClosingAPaneInTheGrid_ShrinksTheLayoutAndKeepsTheRestInOrder()
	{
		ManualSessionManager manager = new();
		using SessionWorkspace workspace = new(manager);
		FakeSession[] tabs = Open(manager, 4);
		workspace.Arrange(PaneArrangement.Grid);

		manager.Remove(tabs[1].Id);

		Assert.Equal("V(P,H(P,P))", PaneLayout.Write(workspace.Layout));
		Assert.Equal([tabs[0].Id], TabsAt(workspace, 0));
		Assert.Equal([tabs[2].Id], TabsAt(workspace, 1));
		Assert.Equal([tabs[3].Id], TabsAt(workspace, 2));
	}

	[Fact]
	public void ClosingTheFocusedPane_HandsTheFocusToThePaneThatTakesItsPlace()
	{
		ManualSessionManager manager = new();
		using SessionWorkspace workspace = new(manager);
		FakeSession[] tabs = Open(manager, 3);
		workspace.Arrange(PaneArrangement.MainAndStack);
		workspace.Activate(tabs[2].Id);

		manager.Remove(tabs[2].Id);

		Assert.Equal("H(P,P)", PaneLayout.Write(workspace.Layout));
		Assert.Equal(tabs[1].Id, workspace.ActiveId);
	}

	[Fact]
	public void ClosingEveryTab_LeavesOnePaneForTheWelcomeScreen()
	{
		ManualSessionManager manager = new();
		using SessionWorkspace workspace = new(manager);
		FakeSession[] tabs = Open(manager, 4);
		workspace.Arrange(PaneArrangement.Grid);

		foreach (FakeSession tab in tabs)
		{
			manager.Remove(tab.Id);
		}

		PaneLeaf only = Assert.IsType<PaneLeaf>(workspace.Layout);
		Assert.Equal(only.PaneId, workspace.FocusedPane);
		Assert.Null(workspace.ActiveId);
	}

	[Fact]
	public void FocusPaneAt_PointsTheCommandsThereAndNamesItsActiveTab()
	{
		ManualSessionManager manager = new();
		using SessionWorkspace workspace = new(manager);
		FakeSession[] tabs = Open(manager, 3);
		workspace.Arrange(PaneArrangement.MainAndStack);

		Assert.Equal(tabs[1].Id, workspace.FocusPaneAt(1));

		Assert.Equal(workspace.Panes[1], workspace.FocusedPane);
		Assert.Equal(tabs[1].Id, workspace.ActiveId);
	}

	[Fact]
	public void FocusPaneAt_NoSuchPane_ChangesNothing()
	{
		ManualSessionManager manager = new();
		using SessionWorkspace workspace = new(manager);
		FakeSession[] tabs = Open(manager, 2);
		workspace.MoveToOtherPane(tabs[1].Id);
		int focused = workspace.FocusedPane;
		int changes = 0;
		workspace.Changed += () => changes++;

		Assert.Null(workspace.FocusPaneAt(2));
		Assert.Null(workspace.FocusPaneAt(-1));
		Assert.Equal(focused, workspace.FocusedPane);
		Assert.Equal(0, changes);
	}

	[Fact]
	public void Arrange_Grid_SpreadsOnePaneOfTabsInTabOrder()
	{
		ManualSessionManager manager = new();
		using SessionWorkspace workspace = new(manager);
		FakeSession[] tabs = Open(manager, 4);
		workspace.Activate(tabs[0].Id);

		Assert.Equal(4, workspace.Arrange(PaneArrangement.Grid));

		Assert.Equal("V(H(P,P),H(P,P))", PaneLayout.Write(workspace.Layout));
		for (int position = 0; position < 4; position++)
		{
			Assert.Equal([tabs[position].Id], TabsAt(workspace, position));
		}

		Assert.Equal(tabs[0].Id, workspace.ActiveId);
	}

	[Fact]
	public void Arrange_MorePanesThanTabs_GetsOnePanePerTab()
	{
		ManualSessionManager manager = new();
		using SessionWorkspace workspace = new(manager);
		Open(manager, 2);

		Assert.Equal(2, workspace.Arrange(PaneArrangement.Grid));

		Assert.Equal("H(P,P)", PaneLayout.Write(workspace.Layout));
	}

	[Fact]
	public void Arrange_Stacked_PutsTheSecondPaneUnderTheFirst()
	{
		ManualSessionManager manager = new();
		using SessionWorkspace workspace = new(manager);
		FakeSession[] tabs = Open(manager, 2);
		workspace.MoveToOtherPane(tabs[1].Id);

		Assert.Equal(2, workspace.Arrange(PaneArrangement.Stacked));

		Assert.Equal("V(P,P)", PaneLayout.Write(workspace.Layout));
		Assert.Equal([tabs[0].Id], TabsAt(workspace, 0));
		Assert.Equal([tabs[1].Id], TabsAt(workspace, 1));
	}

	[Fact]
	public void Arrange_FewerPanes_FoldsTheRestIntoTheLastOne()
	{
		ManualSessionManager manager = new();
		using SessionWorkspace workspace = new(manager);
		FakeSession[] tabs = Open(manager, 4);
		workspace.Arrange(PaneArrangement.Grid);
		workspace.Activate(tabs[3].Id);

		Assert.Equal(2, workspace.Arrange(PaneArrangement.SideBySide));

		Assert.Equal([tabs[0].Id], TabsAt(workspace, 0));
		Assert.Equal([tabs[1].Id, tabs[2].Id, tabs[3].Id], TabsAt(workspace, 1));

		// The tab the commands acted on is still the one showing, and still has the focus.
		Assert.Equal(tabs[3].Id, workspace.ActiveId);
		Assert.Equal(workspace.Panes[1], workspace.FocusedPane);
	}

	[Fact]
	public void Arrange_Single_GathersEveryTabInTheFocusedPaneInTabOrder()
	{
		ManualSessionManager manager = new();
		using SessionWorkspace workspace = new(manager);
		FakeSession[] tabs = Open(manager, 4);
		workspace.Arrange(PaneArrangement.Grid);
		workspace.Activate(tabs[2].Id);
		int focused = workspace.FocusedPane;

		Assert.Equal(1, workspace.Arrange(PaneArrangement.Single));

		PaneLeaf only = Assert.IsType<PaneLeaf>(workspace.Layout);
		Assert.Equal(focused, only.PaneId);
		Assert.Equal(tabs.Select(tab => tab.Id), TabsAt(workspace, 0));
		Assert.Equal(tabs[2].Id, workspace.ActiveId);
	}

	[Fact]
	public void Arrange_NothingOpen_ChangesNothing()
	{
		ManualSessionManager manager = new();
		using SessionWorkspace workspace = new(manager);
		PaneNode before = workspace.Layout;

		Assert.Equal(1, workspace.Arrange(PaneArrangement.Grid));
		Assert.Same(before, workspace.Layout);
	}

	[Fact]
	public void BeginRestore_LaysOutEveryPaneBeforeItsTabArrives()
	{
		ManualSessionManager manager = new();
		using SessionWorkspace workspace = new(manager);
		Guid main = Guid.NewGuid();
		Guid top = Guid.NewGuid();
		Guid bottom = Guid.NewGuid();

		workspace.BeginRestore("H(P,V(P,P))", [new(main, 0), new(bottom, 2), new(top, 1)]);
		Assert.Equal(3, workspace.PaneCount);

		FakeSession mainTab = manager.Add(main);
		FakeSession bottomTab = manager.Add(bottom);
		FakeSession topTab = manager.Add(top);
		workspace.EndRestore();

		Assert.Equal("H(P,V(P,P))", PaneLayout.Write(workspace.Layout));
		Assert.Equal([mainTab.Id], TabsAt(workspace, 0));
		Assert.Equal([topTab.Id], TabsAt(workspace, 1));
		Assert.Equal([bottomTab.Id], TabsAt(workspace, 2));
	}

	[Fact]
	public void EndRestore_DropsThePanesNothingArrivedFor()
	{
		ManualSessionManager manager = new();
		using SessionWorkspace workspace = new(manager);
		Guid arrives = Guid.NewGuid();

		workspace.BeginRestore("V(H(P,P),H(P,P))", [new(arrives, 3), new(Guid.NewGuid(), 1)]);
		FakeSession tab = manager.Add(arrives);
		Assert.Equal(4, workspace.PaneCount);

		workspace.EndRestore();

		PaneLeaf only = Assert.IsType<PaneLeaf>(workspace.Layout);
		Assert.Equal(only.PaneId, workspace.PaneOf(tab.Id));
		Assert.Equal(only.PaneId, workspace.FocusedPane);
		Assert.Equal(tab.Id, workspace.ActiveId);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("H(P,")]
	[InlineData("V(H(P,P),H(P,H(P,P)))")]
	public void BeginRestore_AnUnreadableLayout_PutsEveryTabInOnePane(string? layout)
	{
		ManualSessionManager manager = new();
		using SessionWorkspace workspace = new(manager);
		Guid first = Guid.NewGuid();
		Guid second = Guid.NewGuid();

		workspace.BeginRestore(layout, [new(first, 0), new(second, 1)]);
		manager.Add(first);
		manager.Add(second);
		workspace.EndRestore();

		Assert.IsType<PaneLeaf>(workspace.Layout);
		Assert.Equal(2, TabsAt(workspace, 0).Count());
	}

	// A quick-connect tab opened before the restore has no saved pane; it keeps the one it is in, which comes first.
	[Fact]
	public void BeginRestore_ATabAlreadyOpen_StaysInTheFirstPane()
	{
		ManualSessionManager manager = new();
		using SessionWorkspace workspace = new(manager);
		FakeSession open = manager.Add();
		int pane = workspace.PaneOf(open.Id);
		Guid restored = Guid.NewGuid();

		workspace.BeginRestore("H(P,P)", [new(restored, 1)]);
		FakeSession restoredTab = manager.Add(restored);
		workspace.EndRestore();

		Assert.Equal(pane, workspace.Panes[0]);
		Assert.Equal([open.Id], TabsAt(workspace, 0));
		Assert.Equal([restoredTab.Id], TabsAt(workspace, 1));
	}

	private static FakeSession[] Open(ManualSessionManager manager, int count) =>
		[.. Enumerable.Range(0, count).Select(_ => manager.Add())];

	/// <summary>The tabs of the pane at <paramref name="position"/>, in tab order.</summary>
	private static IEnumerable<Guid> TabsAt(SessionWorkspace workspace, int position) =>
		workspace.TabsIn(workspace.Panes[position]).Select(session => session.Id);
}
