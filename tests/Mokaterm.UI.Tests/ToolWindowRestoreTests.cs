using System.Text.Json;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Components;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Icons;
using Mokaterm.UI.Common.Platform;
using Mokaterm.UI.Common.ToolPages;
using Mokaterm.UI.Shell;
using Mokaterm.UI.Tests.Fakes;

namespace Mokaterm.UI.Tests;

/// <summary>
/// Tool windows that were open when Mokaterm closed come back at the next start: an app tool's after the unlock, a
/// session tool's once the session restore reopened its login and it connected. Ids only reach the plain file. A
/// window the user closed is forgotten; one the app closed is not.
/// </summary>
public sealed class ToolWindowRestoreTests
{
	private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

	private static readonly SessionToolDescriptor Docker = new()
	{
		ProtocolId = "ssh",
		Id = "docker",
		Title = "Docker",
		Icon = MokatermIcons.Modules,
		Component = typeof(NoView),
		Size = SessionToolSize.Wide,
	};

	private static readonly AppToolDescriptor Subnets = new()
	{
		Id = "subnet-calculator",
		Title = "Subnet calculator",
		Icon = MokatermIcons.Globe,
		Component = typeof(NoAppTool),
	};

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task OpeningWindows_StoresIdsOnly_NeverANameOrAnAddress()
	{
		FakeToolWindows windows = new();
		await using ToolHarness harness = await StartAsync(windows);
		FakeSession session = harness.Sessions.Add();

		await harness.AppTools.OpenAsync(Subnets).WaitAsync(Patience, Ct);
		await harness.SessionTools.OpenAsync(session, Docker).WaitAsync(Patience, Ct);

		Assert.Equal(
			[new ToolWindowMemo("subnet-calculator", null), new ToolWindowMemo("docker", session.Connection.Id)],
			harness.UiState.Current.RestoreToolWindows);
		string json = JsonSerializer.Serialize(harness.UiState.Current.RestoreToolWindows);
		Assert.DoesNotContain("build-01", json, StringComparison.Ordinal);
		Assert.DoesNotContain("bc@", json, StringComparison.Ordinal);
		Assert.DoesNotContain(session.Id.ToString(), json, StringComparison.Ordinal);
	}

	[Fact]
	public async Task AQuickConnectSession_LeavesNothingToBringBack()
	{
		FakeToolWindows windows = new();
		await using ToolHarness harness = await StartAsync(windows);
		FakeSession session = new() { IsTransient = true };
		harness.Sessions.Add(session);

		await harness.SessionTools.OpenAsync(session, Docker).WaitAsync(Patience, Ct);

		Assert.Single(windows.Shown);
		Assert.Empty(harness.UiState.Current.RestoreToolWindows);
	}

	[Fact]
	public async Task AWindowTheUserClosed_IsForgotten()
	{
		FakeToolWindows windows = new();
		await using ToolHarness harness = await StartAsync(windows);
		FakeSession session = harness.Sessions.Add();
		await harness.AppTools.OpenAsync(Subnets).WaitAsync(Patience, Ct);
		await harness.SessionTools.OpenAsync(session, Docker).WaitAsync(Patience, Ct);

		windows.CloseByUser(ToolWindowTarget.AppTool("subnet-calculator"));
		harness.SessionTools.ReturnToDialog(session, Docker);

		Assert.Empty(harness.UiState.Current.RestoreToolWindows);
	}

	[Fact]
	public async Task ClosingTheTab_ForgetsItsToolWindowsToo()
	{
		FakeToolWindows windows = new();
		await using ToolHarness harness = await StartAsync(windows);
		FakeSession session = harness.Sessions.Add();
		await harness.SessionTools.OpenAsync(session, Docker).WaitAsync(Patience, Ct);

		harness.CloseTab(session.Id);

		Assert.Contains(session.Id, windows.ClosedSessions);
		Assert.Empty(harness.UiState.Current.RestoreToolWindows);
	}

	// Mokaterm exiting closes every session and every window on its own; none of that is the user closing anything.
	[Fact]
	public async Task TheAppClosingItsWindows_KeepsThem()
	{
		FakeToolWindows windows = new();
		await using ToolHarness harness = await StartAsync(windows);
		FakeSession session = harness.Sessions.Add();
		await harness.AppTools.OpenAsync(Subnets).WaitAsync(Patience, Ct);
		await harness.SessionTools.OpenAsync(session, Docker).WaitAsync(Patience, Ct);

		harness.Sessions.Remove(session.Id);
		harness.Windows.Dispose();

		Assert.Empty(windows.Shown);
		Assert.Equal(2, harness.UiState.Current.RestoreToolWindows.Count);
	}

	[Fact]
	public async Task AtStart_AnAppToolsWindowComesBack_AndTheMainWindowKeepsTheKeyboard()
	{
		FakeToolWindows windows = new();
		await using ToolHarness harness = await StartAsync(windows, Saved(new ToolWindowMemo("subnet-calculator", null)));

		ToolWindowRequest request = Assert.Single(windows.Opened);
		Assert.Equal(ToolWindowTarget.AppTool("subnet-calculator"), request.Target);
		Assert.Equal("Subnet calculator · Mokaterm", request.Title);
		Assert.Equal(1, windows.OwnerShown);
	}

	// Even with tools set to open in dialogs: it was in a window when Mokaterm closed, so that is where it comes back.
	[Fact]
	public async Task AtStart_AWindowComesBack_WhateverTheSettingSaysNow()
	{
		FakeToolWindows windows = new();
		await using ToolHarness harness = await ToolHarness.CreateAsync(
			windows,
			new AppearanceSettings { ToolsInOwnWindow = false },
			saved: Saved(new ToolWindowMemo("subnet-calculator", null)),
			sessionTools: [Docker],
			appTools: [Subnets]);
		await harness.StartAsync().WaitAsync(Patience, Ct);

		Assert.Single(windows.Opened);
		Assert.Null(harness.AppTools.Dialog);
	}

	[Fact]
	public async Task AtStart_AToolAPluginNoLongerAdds_IsDropped()
	{
		FakeToolWindows windows = new();
		await using ToolHarness harness = await StartAsync(windows, Saved(new ToolWindowMemo("password-generator", null)));

		Assert.Empty(windows.Opened);
		Assert.Empty(harness.UiState.Current.RestoreToolWindows);
	}

	[Fact]
	public async Task AtStart_ASessionToolsWindow_WaitsForItsSessionToConnect()
	{
		FakeToolWindows windows = new();
		Guid login = Guid.NewGuid();
		ConnectingSessions sessions = new();
		await using ToolHarness harness = await StartAsync(
			windows,
			Saved(new ToolWindowMemo("docker", login)) with { RestoreConnectionIds = [login] },
			harness => sessions.Attach(harness));

		FakeSession session = sessions.Reopened.Single();
		Assert.Empty(windows.Opened);

		session.Become(SessionState.Connected);

		ToolWindowRequest request = Assert.Single(windows.Opened);
		Assert.Equal(ToolWindowTarget.SessionTool(session.Id, "docker"), request.Target);
		Assert.Equal([new ToolWindowMemo("docker", login)], harness.UiState.Current.RestoreToolWindows);
	}

	[Fact]
	public async Task AtStart_ASessionThatAlreadyConnected_GetsItsWindowAtOnce()
	{
		FakeToolWindows windows = new();
		Guid login = Guid.NewGuid();
		await using ToolHarness harness = await StartAsync(
			windows,
			Saved(new ToolWindowMemo("docker", login)) with { RestoreConnectionIds = [login] });

		Assert.Equal(harness.Sessions.Sessions.Single().Id, Assert.Single(windows.Opened).Target.SessionId);
	}

	[Fact]
	public async Task AtStart_ASessionThatNeverComesBack_TakesItsWindowWithIt()
	{
		FakeToolWindows windows = new();
		Guid login = Guid.NewGuid();
		ToolHarness harness = await ToolHarness.CreateAsync(
			windows,
			saved: Saved(new ToolWindowMemo("docker", login)) with { RestoreConnectionIds = [login] },
			sessionTools: [Docker],
			appTools: [Subnets]);
		await using (harness)
		{
			// A login deleted since the last run: the session restore cannot reopen it.
			harness.Sessions.Missing.Add(login);
			await harness.StartAsync().WaitAsync(Patience, Ct);

			Assert.Empty(windows.Opened);
			Assert.Empty(harness.UiState.Current.RestoreToolWindows);
		}
	}

	[Fact]
	public async Task AtStart_SessionsNotRestored_TakeTheirWindowsWithThem()
	{
		FakeToolWindows windows = new();
		Guid login = Guid.NewGuid();
		await using ToolHarness harness = await ToolHarness.CreateAsync(
			windows,
			restoreMode: SessionRestoreMode.Off,
			saved: Saved(new ToolWindowMemo("docker", login), new ToolWindowMemo("subnet-calculator", null)) with { RestoreConnectionIds = [login] },
			sessionTools: [Docker],
			appTools: [Subnets]);
		await harness.StartAsync().WaitAsync(Patience, Ct);

		// The app tool needs no session, so it comes back however sessions are restored.
		Assert.Equal(ToolWindowTarget.AppTool("subnet-calculator"), Assert.Single(windows.Opened).Target);
		Assert.Equal([new ToolWindowMemo("subnet-calculator", null)], harness.UiState.Current.RestoreToolWindows);
	}

	// Mokaterm offers the sessions and the user has not answered: both wait, in the file as well.
	[Fact]
	public async Task AtStart_AnOfferNotYetAnswered_KeepsTheWindowsWaiting_UntilItIsTurnedDown()
	{
		FakeToolWindows windows = new();
		Guid login = Guid.NewGuid();
		await using ToolHarness harness = await ToolHarness.CreateAsync(
			windows,
			restoreMode: SessionRestoreMode.Ask,
			saved: Saved(new ToolWindowMemo("docker", login)) with { RestoreConnectionIds = [login] },
			sessionTools: [Docker]);
		await harness.StartAsync().WaitAsync(Patience, Ct);

		Assert.Equal([new ToolWindowMemo("docker", login)], harness.UiState.Current.RestoreToolWindows);

		harness.Restore.Dismiss();

		Assert.Empty(windows.Opened);
		Assert.Empty(harness.UiState.Current.RestoreToolWindows);
	}

	[Fact]
	public async Task AtStart_AnOfferTakenUp_BringsTheWindowsBack()
	{
		FakeToolWindows windows = new();
		Guid login = Guid.NewGuid();
		await using ToolHarness harness = await ToolHarness.CreateAsync(
			windows,
			restoreMode: SessionRestoreMode.Ask,
			saved: Saved(new ToolWindowMemo("docker", login)) with { RestoreConnectionIds = [login] },
			sessionTools: [Docker]);
		await harness.StartAsync().WaitAsync(Patience, Ct);

		await harness.Restore.RestoreAsync().WaitAsync(Patience, Ct);

		Assert.Single(windows.Opened);
	}

	// The vault locking closes the windows; that was the app, so the next unlock brings them back.
	[Fact]
	public async Task AnUnlockAfterALock_BringsBackTheWindowsTheLockClosed()
	{
		FakeToolWindows windows = new();
		await using ToolHarness harness = await StartAsync(windows);
		FakeSession session = harness.Sessions.Add();
		await harness.AppTools.OpenAsync(Subnets).WaitAsync(Patience, Ct);
		await harness.SessionTools.OpenAsync(session, Docker).WaitAsync(Patience, Ct);

		harness.Vault.Lock();
		Assert.Empty(windows.Shown);

		await harness.ToolRestore.StartAsync().WaitAsync(Patience, Ct);

		Assert.Equal(2, windows.Shown.Count);
		Assert.Equal(2, harness.UiState.Current.RestoreToolWindows.Count);
	}

	[Fact]
	public async Task ReopeningOff_BringsNothingBack_AndForgetsWhatWasRemembered()
	{
		FakeToolWindows windows = new();
		Guid login = Guid.NewGuid();
		await using ToolHarness harness = await ToolHarness.CreateAsync(
			windows,
			new AppearanceSettings { ReopenToolWindows = false },
			saved: Saved(new ToolWindowMemo("subnet-calculator", null), new ToolWindowMemo("docker", login)) with { RestoreConnectionIds = [login] },
			sessionTools: [Docker],
			appTools: [Subnets]);
		await harness.StartAsync().WaitAsync(Patience, Ct);

		Assert.Empty(windows.Opened);
		Assert.Single(harness.Sessions.Sessions);
		Assert.Empty(harness.UiState.Current.RestoreToolWindows);

		// What opens from now on is followed as before, so turning it back on brings back what was open at the close.
		await harness.AppTools.OpenAsync(Subnets).WaitAsync(Patience, Ct);
		Assert.Equal([new ToolWindowMemo("subnet-calculator", null)], harness.UiState.Current.RestoreToolWindows);
	}

	// The lock closed the windows while reopening was off: the unlock leaves them shut and forgets them.
	[Fact]
	public async Task ReopeningTurnedOff_BeforeAnUnlock_LeavesTheWindowsTheLockClosed()
	{
		FakeToolWindows windows = new();
		await using ToolHarness harness = await StartAsync(windows);
		await harness.AppTools.OpenAsync(Subnets).WaitAsync(Patience, Ct);
		harness.Vault.Lock();

		await harness.Settings.UpdateAsync<AppearanceSettings>(s => s with { ReopenToolWindows = false }, Ct);
		await harness.ToolRestore.StartAsync().WaitAsync(Patience, Ct);

		Assert.Empty(windows.Shown);
		Assert.Empty(harness.UiState.Current.RestoreToolWindows);
	}

	[Fact]
	public async Task NoWindowHost_RemembersNothing_AndBringsNothingBack()
	{
		await using ToolHarness harness = await ToolHarness.CreateAsync(
			windows: null,
			saved: Saved(new ToolWindowMemo("subnet-calculator", null)),
			appTools: [Subnets]);
		await harness.StartAsync().WaitAsync(Patience, Ct);

		await harness.AppTools.OpenAsync(Subnets).WaitAsync(Patience, Ct);

		Assert.Same(Subnets, harness.AppTools.Dialog);
		Assert.Equal([new ToolWindowMemo("subnet-calculator", null)], harness.UiState.Current.RestoreToolWindows);
	}

	private static UiStateDocument Saved(params ToolWindowMemo[] memos) => new() { RestoreToolWindows = memos };

	private static async Task<ToolHarness> StartAsync(
		FakeToolWindows windows,
		UiStateDocument? saved = null,
		Action<ToolHarness>? before = null)
	{
		ToolHarness harness = await ToolHarness.CreateAsync(windows, saved: saved, sessionTools: [Docker], appTools: [Subnets]);
		before?.Invoke(harness);
		await harness.StartAsync().WaitAsync(Patience, Ct);
		return harness;
	}

	/// <summary>Makes every session the restore reopens start out connecting, the way a real one does.</summary>
	private sealed class ConnectingSessions
	{
		public List<FakeSession> Reopened { get; } = [];

		public void Attach(ToolHarness harness) => harness.Restore.Reopened += session =>
		{
			FakeSession fake = (FakeSession)session;
			Reopened.Add(fake);
			_ = fake.Become(SessionState.Connecting);
		};
	}

	private sealed class NoView : SessionViewBase;

	private sealed class NoAppTool : AppToolBase;
}
