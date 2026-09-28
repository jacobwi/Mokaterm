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

		public static async Task<Harness> StartAsync(SessionRestoreMode mode, params Guid[] saved)
		{
			MemoryAppDataStore documents = new();
			if (saved.Length > 0)
			{
				documents.Set("ui-state", new UiStateDocument { RestoreConnectionIds = saved });
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
