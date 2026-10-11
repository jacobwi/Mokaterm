using Microsoft.Extensions.Logging.Abstractions;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Storage;
using Mokaterm.UI.Interaction;
using Mokaterm.UI.Shell;
using Mokaterm.UI.Tests.Fakes;
using Mokaterm.UI.Workspace;

namespace Mokaterm.UI.Tests;

public sealed class SessionRestoreTests
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task StartAsync_SavedSet_IsOfferedAndKept()
	{
		Guid first = Guid.NewGuid();
		Guid second = Guid.NewGuid();
		using Harness harness = await Harness.StartAsync(SessionRestoreMode.Ask, first, second);

		Assert.Equal([first, second], harness.Restore.Pending);
		Assert.Equal([first, second], harness.UiState.Current.RestoreConnectionIds);
	}

	[Fact]
	public async Task StartAsync_TurnedOff_OffersNothing()
	{
		using Harness harness = await Harness.StartAsync(SessionRestoreMode.Off, Guid.NewGuid());

		Assert.Empty(harness.Restore.Pending);
	}

	[Fact]
	public async Task OpeningASession_PutsItsLoginInTheSavedSet()
	{
		using Harness harness = await Harness.StartAsync(SessionRestoreMode.Ask);
		Guid connectionId = Guid.NewGuid();

		harness.Manager.Add(new FakeSession(connectionId));

		Assert.Equal([connectionId], harness.UiState.Current.RestoreConnectionIds);
	}

	[Fact]
	public async Task SessionsGoingAwayWithoutACloseKeepTheSavedSet()
	{
		using Harness harness = await Harness.StartAsync(SessionRestoreMode.Ask);
		Guid connectionId = Guid.NewGuid();
		FakeSession session = new(connectionId);
		harness.Manager.Add(session);

		// A window or circuit going away closes sessions without anyone pressing Close.
		harness.Manager.Remove(session.Id);

		Assert.Equal([connectionId], harness.UiState.Current.RestoreConnectionIds);
	}

	[Fact]
	public async Task Forget_DropsTheTabTheUserClosed()
	{
		using Harness harness = await Harness.StartAsync(SessionRestoreMode.Ask);
		FakeSession kept = new();
		FakeSession closed = new();
		harness.Manager.Add(kept);
		harness.Manager.Add(closed);

		harness.Restore.Forget(closed.Id);
		harness.Manager.Remove(closed.Id);

		Assert.Equal([kept.Connection.Id], harness.UiState.Current.RestoreConnectionIds);
	}

	[Fact]
	public async Task RestoreAsync_OpensTheSavedLoginsInOrder()
	{
		Guid first = Guid.NewGuid();
		Guid second = Guid.NewGuid();
		using Harness harness = await Harness.StartAsync(SessionRestoreMode.Ask, first, second);

		await harness.Restore.RestoreAsync().WaitAsync(TimeSpan.FromSeconds(5), Ct);

		Assert.Equal([first, second], harness.Manager.Opened);
		Assert.Empty(harness.Restore.Pending);
		Assert.Equal([first, second], harness.UiState.Current.RestoreConnectionIds);
	}

	[Fact]
	public async Task Dismiss_ClearsTheOfferAndTheSavedSet()
	{
		using Harness harness = await Harness.StartAsync(SessionRestoreMode.Ask, Guid.NewGuid());

		harness.Restore.Dismiss();

		Assert.Empty(harness.Restore.Pending);
		Assert.Empty(harness.UiState.Current.RestoreConnectionIds);
	}

	[Fact]
	public async Task StartAsync_Always_ReopensWithoutBeingAsked()
	{
		Guid connectionId = Guid.NewGuid();
		using Harness harness = await Harness.StartAsync(SessionRestoreMode.Always, connectionId);

		Assert.Equal([connectionId], harness.Manager.Opened);
		Assert.Empty(harness.Restore.Pending);
	}

	[Fact]
	public async Task SplitTabs_SaveTheirPanesAndTheLayout()
	{
		using Harness harness = await Harness.StartAsync(SessionRestoreMode.Ask);
		FakeSession first = harness.Manager.Add();
		FakeSession second = harness.Manager.Add();
		FakeSession third = harness.Manager.Add();

		harness.Workspace.Arrange(PaneArrangement.MainAndStack);

		UiStateDocument saved = harness.UiState.Current;
		Assert.Equal([first.Connection.Id, second.Connection.Id, third.Connection.Id], saved.RestoreConnectionIds);
		Assert.Equal([0, 1, 2], saved.RestorePanes);
		Assert.Equal("H(P,V(P,P))", saved.RestoreLayout);
	}

	// One pane is what a restore does anyway, so the file says nothing more than the ids.
	[Fact]
	public async Task TabsInOnePane_SaveNoLayout()
	{
		using Harness harness = await Harness.StartAsync(SessionRestoreMode.Ask);
		FakeSession first = harness.Manager.Add();
		FakeSession second = harness.Manager.Add();
		harness.Workspace.MoveToOtherPane(second.Id);

		harness.Workspace.Arrange(PaneArrangement.Single);

		UiStateDocument saved = harness.UiState.Current;
		Assert.Equal([first.Connection.Id, second.Connection.Id], saved.RestoreConnectionIds);
		Assert.Empty(saved.RestorePanes);
		Assert.Null(saved.RestoreLayout);
	}

	// A quick-connect tab is not saved, so neither is a pane that holds nothing else.
	[Fact]
	public async Task APaneOfQuickConnectTabs_IsLeftOutOfTheLayout()
	{
		using Harness harness = await Harness.StartAsync(SessionRestoreMode.Ask);
		FakeSession saved = harness.Manager.Add();
		FakeSession quick = new() { IsTransient = true };
		harness.Manager.Add(quick);

		harness.Workspace.MoveToOtherPane(quick.Id);

		Assert.Equal(2, harness.Workspace.PaneCount);
		Assert.Equal([saved.Connection.Id], harness.UiState.Current.RestoreConnectionIds);
		Assert.Null(harness.UiState.Current.RestoreLayout);
	}

	[Fact]
	public async Task AWindowClosing_KeepsTheTabsInOrderAndTheLayoutTheyWereIn()
	{
		using Harness harness = await Harness.StartAsync(SessionRestoreMode.Ask);
		FakeSession[] tabs = [.. Enumerable.Range(0, 4).Select(_ => harness.Manager.Add())];
		harness.Workspace.Arrange(PaneArrangement.Grid);

		// A closing window ends its sessions one at a time, in no set order, and each pane gives way as it empties.
		foreach (int index in (int[])[2, 0, 3, 1])
		{
			harness.Manager.Remove(tabs[index].Id);
		}

		UiStateDocument saved = harness.UiState.Current;
		Assert.Equal(tabs.Select(tab => tab.Connection.Id), saved.RestoreConnectionIds);
		Assert.Equal([0, 1, 2, 3], saved.RestorePanes);
		Assert.Equal("V(H(P,P),H(P,P))", saved.RestoreLayout);
	}

	[Fact]
	public async Task StartAsync_SavedPanes_AreKeptWhileTheOfferIsOpen()
	{
		Guid first = Guid.NewGuid();
		Guid second = Guid.NewGuid();
		using Harness harness = await Harness.StartAsync(
			SessionRestoreMode.Ask,
			new UiStateDocument { RestoreConnectionIds = [first, second], RestorePanes = [0, 1], RestoreLayout = "V(P,P)" });

		UiStateDocument saved = harness.UiState.Current;
		Assert.Equal([first, second], saved.RestoreConnectionIds);
		Assert.Equal([0, 1], saved.RestorePanes);
		Assert.Equal("V(P,P)", saved.RestoreLayout);
	}

	[Fact]
	public async Task RestoreAsync_PutsEachLoginBackInItsPane()
	{
		Guid main = Guid.NewGuid();
		Guid bottom = Guid.NewGuid();
		Guid top = Guid.NewGuid();
		using Harness harness = await Harness.StartAsync(
			SessionRestoreMode.Ask,
			new UiStateDocument { RestoreConnectionIds = [main, bottom, top], RestorePanes = [0, 2, 1], RestoreLayout = "H(P,V(P,P))" });

		await harness.Restore.RestoreAsync().WaitAsync(TimeSpan.FromSeconds(5), Ct);

		Assert.Equal("H(P,V(P,P))", PaneLayout.Write(harness.Workspace.Layout));
		Assert.Equal([main], LoginsAt(harness.Workspace, 0));
		Assert.Equal([top], LoginsAt(harness.Workspace, 1));
		Assert.Equal([bottom], LoginsAt(harness.Workspace, 2));

		// Saved again as it came back, so the next start finds the same.
		Assert.Equal([main, bottom, top], harness.UiState.Current.RestoreConnectionIds);
		Assert.Equal([0, 2, 1], harness.UiState.Current.RestorePanes);
		Assert.Equal("H(P,V(P,P))", harness.UiState.Current.RestoreLayout);
	}

	[Fact]
	public async Task RestoreAsync_ALoginThatIsGone_LeavesNoEmptyPane()
	{
		Guid left = Guid.NewGuid();
		Guid gone = Guid.NewGuid();
		Guid right = Guid.NewGuid();
		using Harness harness = await Harness.StartAsync(
			SessionRestoreMode.Ask,
			new UiStateDocument { RestoreConnectionIds = [left, gone, right], RestorePanes = [0, 1, 2], RestoreLayout = "H(P,V(P,P))" });
		harness.Manager.Missing.Add(gone);

		await harness.Restore.RestoreAsync().WaitAsync(TimeSpan.FromSeconds(5), Ct);

		Assert.Equal("H(P,P)", PaneLayout.Write(harness.Workspace.Layout));
		Assert.Equal([left], LoginsAt(harness.Workspace, 0));
		Assert.Equal([right], LoginsAt(harness.Workspace, 1));
	}

	// A file from before panes were saved, or one edited by hand, still reopens everything, in one pane.
	[Fact]
	public async Task RestoreAsync_NoLayoutSaved_OpensEverythingInOnePane()
	{
		Guid first = Guid.NewGuid();
		Guid second = Guid.NewGuid();
		using Harness harness = await Harness.StartAsync(
			SessionRestoreMode.Ask,
			new UiStateDocument { RestoreConnectionIds = [first, second], RestorePanes = [3, 7], RestoreLayout = "not a layout" });

		await harness.Restore.RestoreAsync().WaitAsync(TimeSpan.FromSeconds(5), Ct);

		Assert.IsType<PaneLeaf>(harness.Workspace.Layout);
		Assert.Equal([first, second], LoginsAt(harness.Workspace, 0));
	}

	private static IEnumerable<Guid> LoginsAt(SessionWorkspace workspace, int position) =>
		workspace.TabsIn(workspace.Panes[position]).Select(session => session.Connection.Id);

	private sealed class Harness : IDisposable
	{
		private Harness(ManualSessionManager manager, SessionWorkspace workspace, UiStateStore uiState, SessionRestore restore)
		{
			Manager = manager;
			Workspace = workspace;
			UiState = uiState;
			Restore = restore;
		}

		public ManualSessionManager Manager { get; }

		public SessionWorkspace Workspace { get; }

		public UiStateStore UiState { get; }

		public SessionRestore Restore { get; }

		public static Task<Harness> StartAsync(SessionRestoreMode mode, params Guid[] saved) =>
			StartAsync(mode, saved.Length > 0 ? new UiStateDocument { RestoreConnectionIds = saved } : null);

		public static async Task<Harness> StartAsync(SessionRestoreMode mode, UiStateDocument? saved)
		{
			MemoryAppDataStore documents = new();
			if (saved is not null)
			{
				documents.Set("ui-state", saved);
			}

			UiStateStore uiState = new(documents, TimeProvider.System, NullLogger<UiStateStore>.Instance);
			await uiState.LoadAsync(Ct);

			ManualSessionManager manager = new();
			SessionWorkspace workspace = new(manager);
			SessionRestore restore = new(
				manager,
				workspace,
				uiState,
				new FixedSettings(new GeneralSettings { RestoreSessions = mode }),
				new UserInteractionService(),
				NullLogger<SessionRestore>.Instance);

			await restore.StartAsync().WaitAsync(TimeSpan.FromSeconds(5), Ct);
			return new Harness(manager, workspace, uiState, restore);
		}

		public void Dispose()
		{
			Restore.Dispose();
			Workspace.Dispose();
		}
	}
}
