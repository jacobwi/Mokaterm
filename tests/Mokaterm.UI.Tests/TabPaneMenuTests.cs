using Microsoft.Extensions.Logging.Abstractions;
using Moka.Red.ContextMenu;
using Mokaterm.UI.Interaction;
using Mokaterm.UI.Shell;
using Mokaterm.UI.Tests.Fakes;
using Mokaterm.UI.Workspace;

namespace Mokaterm.UI.Tests;

/// <summary>
/// The tab menu's "Move to pane" entries: one per pane with the tab's own checked, a new pane on every side while there
/// is room, and every tab back in one pane while there is more than one.
/// </summary>
public sealed class TabPaneMenuTests
{
	[Fact]
	public void TabMenu_HasTheMoveToPaneEntries()
	{
		using Harness harness = new();
		FakeSession session = harness.Manager.Add();

		MokaContextMenuItem move = Assert.Single(harness.Actions.BuildTabMenu(session), item => item.Text == "Move to pane");

		Assert.True(move.HasChildren);
	}

	[Fact]
	public void OnePane_OffersNewPanesAndNothingToJoin()
	{
		using Harness harness = new();
		FakeSession first = harness.Manager.Add();
		harness.Manager.Add();

		IReadOnlyList<MokaContextMenuItem> items = harness.Actions.BuildPaneItems(first);

		MokaContextMenuItem pane = Assert.Single(items, item => item.Text.StartsWith("Pane ", StringComparison.Ordinal));
		Assert.True(pane.Checked);
		Assert.True(pane.Disabled);
		Assert.All(items.Where(item => item.Text.StartsWith("New pane", StringComparison.Ordinal)), item => Assert.False(item.Disabled));
		Assert.True(Assert.Single(items, item => item.Text == "Every tab in one pane").Disabled);
	}

	[Fact]
	public void PaneEntries_MoveTheTabThere()
	{
		using Harness harness = new();
		FakeSession[] tabs = [.. Enumerable.Range(0, 4).Select(_ => harness.Manager.Add())];
		harness.Workspace.Arrange(PaneArrangement.MainAndStack);

		MokaContextMenuItem target = Assert.Single(harness.Actions.BuildPaneItems(tabs[0]), item => item.Text == "Pane 3");
		target.OnClickSync!();

		// The first pane keeps its other tab, so the layout stays as it was.
		Assert.Equal(3, harness.Workspace.PaneCount);
		Assert.Equal(2, harness.Workspace.PositionOf(harness.Workspace.PaneOf(tabs[0].Id)));
		Assert.Equal(harness.Workspace.PaneOf(tabs[2].Id), harness.Workspace.PaneOf(tabs[0].Id));
		Assert.Equal(tabs[0].Id, harness.Workspace.ActiveId);
	}

	[Fact]
	public void ATabAloneInItsPane_CannotSplitItOff()
	{
		using Harness harness = new();
		harness.Manager.Add();
		FakeSession alone = harness.Manager.Add();
		harness.Workspace.MoveToOtherPane(alone.Id);

		IReadOnlyList<MokaContextMenuItem> items = harness.Actions.BuildPaneItems(alone);

		Assert.Equal(["Pane 1", "Pane 2"], items.Where(item => item.Text.StartsWith("Pane ", StringComparison.Ordinal)).Select(item => item.Text));
		Assert.All(items.Where(item => item.Text.StartsWith("New pane", StringComparison.Ordinal)), item => Assert.True(item.Disabled));
		Assert.False(Assert.Single(items, item => item.Text == "Every tab in one pane").Disabled);
	}

	[Fact]
	public void FourPanes_OfferNoFifth()
	{
		using Harness harness = new();
		FakeSession[] tabs = [.. Enumerable.Range(0, 5).Select(_ => harness.Manager.Add())];
		harness.Workspace.Arrange(PaneArrangement.Grid);

		IReadOnlyList<MokaContextMenuItem> items = harness.Actions.BuildPaneItems(tabs[0]);

		Assert.Equal(4, items.Count(item => item.Text.StartsWith("Pane ", StringComparison.Ordinal)));
		Assert.All(items.Where(item => item.Text.StartsWith("New pane", StringComparison.Ordinal)), item => Assert.True(item.Disabled));
	}

	[Fact]
	public void EveryTabInOnePane_EndsTheSplit()
	{
		using Harness harness = new();
		FakeSession[] tabs = [.. Enumerable.Range(0, 3).Select(_ => harness.Manager.Add())];
		harness.Workspace.Arrange(PaneArrangement.MainAndStack);

		Assert.Single(harness.Actions.BuildPaneItems(tabs[1]), item => item.Text == "Every tab in one pane").OnClickSync!();

		Assert.False(harness.Workspace.IsSplit);
		Assert.Equal(3, harness.Workspace.TabsIn(harness.Workspace.FocusedPane).Count);
	}

	private sealed class Harness : IDisposable
	{
		public Harness()
		{
			UiStateStore uiState = new(new MemoryAppDataStore(), TimeProvider.System, NullLogger<UiStateStore>.Instance);
			FixedSettings settings = new();
			UserInteractionService interaction = new();
			Workspace = new SessionWorkspace(Manager);
			Restore = new SessionRestore(Manager, Workspace, uiState, settings, interaction, NullLogger<SessionRestore>.Instance);
			Actions = new SessionActions(
				Manager,
				Workspace,
				Restore,
				new NoProtocols(),
				interaction,
				settings,
				new ShellState(uiState),
				NullLogger<SessionActions>.Instance);
		}

		public ManualSessionManager Manager { get; } = new();

		public SessionWorkspace Workspace { get; }

		public SessionRestore Restore { get; }

		public SessionActions Actions { get; }

		public void Dispose()
		{
			Restore.Dispose();
			Workspace.Dispose();
		}
	}
}
