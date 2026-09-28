using Mokaterm.Abstractions.Sessions;
using Mokaterm.UI.Tests.Fakes;
using Mokaterm.UI.Workspace;

namespace Mokaterm.UI.Tests;

public sealed class SessionWorkspaceTests
{
	[Fact]
	public void NewSessions_JoinThePaneTheUserIsWorkingIn()
	{
		ManualSessionManager manager = new();
		using SessionWorkspace workspace = new(manager);
		FakeSession first = manager.Add();
		workspace.MoveToOtherPane(first.Id);

		FakeSession second = manager.Add();

		Assert.Equal(1, workspace.PaneOf(second.Id));
		Assert.Equal(1, workspace.FocusedPane);
		Assert.False(workspace.IsSplit);
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
		Assert.Equal([stays.Id], workspace.TabsIn(0).Select(session => session.Id));
		Assert.Equal([moves.Id], workspace.TabsIn(1).Select(session => session.Id));
		Assert.Equal(1, workspace.FocusedPane);
		Assert.Equal(moves.Id, workspace.ActiveId);
		Assert.Equal(stays.Id, workspace.ActiveIdIn(0));
	}

	[Fact]
	public void MoveToOtherPane_OnTheLastTabThere_EndsTheSplit()
	{
		ManualSessionManager manager = new();
		using SessionWorkspace workspace = new(manager);
		manager.Add();
		FakeSession moved = manager.Add();
		workspace.MoveToOtherPane(moved.Id);

		workspace.MoveToOtherPane(moved.Id);

		Assert.False(workspace.IsSplit);
		Assert.Equal(0, workspace.PaneOf(moved.Id));
		Assert.Equal(0, workspace.FocusedPane);
	}

	[Fact]
	public void ClosingTheLastTabOfAPane_EndsTheSplitAndMovesTheFocus()
	{
		ManualSessionManager manager = new();
		using SessionWorkspace workspace = new(manager);
		FakeSession left = manager.Add();
		FakeSession right = manager.Add();
		workspace.MoveToOtherPane(right.Id);

		manager.Remove(right.Id);

		Assert.False(workspace.IsSplit);
		Assert.Equal(0, workspace.FocusedPane);
		Assert.Equal(left.Id, workspace.ActiveId);
		Assert.Null(workspace.ActiveIdIn(1));
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
		workspace.FocusPane(0);
		workspace.Activate(second.Id);

		manager.Remove(second.Id);

		Assert.Equal(first.Id, workspace.ActiveIdIn(0));
		Assert.Equal(third.Id, workspace.ActiveIdIn(1));
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

		Assert.Equal([third.Id, first.Id, second.Id], workspace.TabsIn(0).Select(session => session.Id));
		Assert.Equal([other.Id], workspace.TabsIn(1).Select(session => session.Id));
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
		workspace.FocusPane(0);
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
		workspace.FocusPane(0);

		workspace.Activate(right.Id);

		Assert.Equal(1, workspace.FocusedPane);
		Assert.Equal(right.Id, workspace.ActiveId);
		Assert.Equal(left.Id, workspace.ActiveIdIn(0));
	}

	[Fact]
	public void OpenOrder_KeepsEveryTabOfBothPanes()
	{
		ManualSessionManager manager = new();
		using SessionWorkspace workspace = new(manager);
		FakeSession first = manager.Add();
		FakeSession second = manager.Add();
		workspace.MoveToOtherPane(second.Id);

		Assert.Equal([first.Id, second.Id], workspace.OpenOrder.Select(session => session.Id));
		Assert.Equal(2, workspace.Tabs.Count);
	}
}
