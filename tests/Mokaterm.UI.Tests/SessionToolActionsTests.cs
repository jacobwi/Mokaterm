using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Components;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Icons;
using Mokaterm.UI.Common.Platform;
using Mokaterm.UI.Tests.Fakes;

namespace Mokaterm.UI.Tests;

/// <summary>
/// Where a session tool opens and when it has to go: a dialog unless the host has windows and the setting asks for
/// them, a window that falls back to the dialog, and windows that close with their session, the vault or the window
/// that opened them.
/// </summary>
public sealed class SessionToolActionsTests
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

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task NoWindowHost_OpensADialog_AndTheButtonClosesItAgain()
	{
		await using ToolHarness harness = await CreateAsync(windows: null);
		FakeSession session = harness.Sessions.Add();

		await harness.SessionTools.ToggleAsync(session, Docker).WaitAsync(Patience, Ct);
		Assert.Equal(Docker, harness.SessionTools.DialogFor(session));
		Assert.True(harness.SessionTools.IsOpen(session, Docker));

		await harness.SessionTools.ToggleAsync(session, Docker).WaitAsync(Patience, Ct);
		Assert.Null(harness.SessionTools.DialogFor(session));
	}

	[Fact]
	public async Task WindowHost_SettingOn_OpensAWindowTitledWithTheToolAndTheSession()
	{
		FakeToolWindows windows = new();
		await using ToolHarness harness = await CreateAsync(windows);
		FakeSession session = harness.Sessions.Add();

		await harness.SessionTools.OpenAsync(session, Docker).WaitAsync(Patience, Ct);

		ToolWindowRequest request = Assert.Single(windows.Opened);
		Assert.Equal("Docker · bc@build-01.example.com", request.Title);
		Assert.Equal(ToolWindowTarget.SessionTool(session.Id, "docker"), request.Target);
		Assert.Equal(SessionToolSize.Wide, request.Size);
		Assert.Same(harness.Services, request.Services);
		Assert.Null(harness.SessionTools.DialogFor(session));
	}

	[Fact]
	public async Task WindowHost_SettingOff_KeepsTheDialog()
	{
		FakeToolWindows windows = new();
		await using ToolHarness harness = await CreateAsync(windows, new AppearanceSettings { ToolsInOwnWindow = false });
		FakeSession session = harness.Sessions.Add();

		await harness.SessionTools.OpenAsync(session, Docker).WaitAsync(Patience, Ct);

		Assert.Empty(windows.Opened);
		Assert.Equal(Docker, harness.SessionTools.DialogFor(session));
	}

	[Fact]
	public async Task AWindowThatWillNotOpen_FallsBackToTheDialog()
	{
		FakeToolWindows windows = new() { Opens = false };
		await using ToolHarness harness = await CreateAsync(windows);
		FakeSession session = harness.Sessions.Add();

		await harness.SessionTools.OpenAsync(session, Docker).WaitAsync(Patience, Ct);

		Assert.Equal(Docker, harness.SessionTools.DialogFor(session));
	}

	[Fact]
	public async Task PopOut_MovesTheDialogToAWindow()
	{
		FakeToolWindows windows = new();
		await using ToolHarness harness = await CreateAsync(windows, new AppearanceSettings { ToolsInOwnWindow = false });
		FakeSession session = harness.Sessions.Add();
		await harness.SessionTools.OpenAsync(session, Docker).WaitAsync(Patience, Ct);

		await harness.SessionTools.PopOutAsync(session, Docker).WaitAsync(Patience, Ct);

		Assert.Single(windows.Opened);
		Assert.Null(harness.SessionTools.DialogFor(session));
		Assert.True(harness.SessionTools.IsInWindow(session, Docker));
	}

	// "Back to the main window": the dialog over the session's tab, the tab in front, and the window gone for good.
	[Fact]
	public async Task ReturnToDialog_ClosesTheWindow_ShowsTheDialog_AndBringsTheTabForward()
	{
		FakeToolWindows windows = new();
		await using ToolHarness harness = await CreateAsync(windows);
		FakeSession session = harness.Sessions.Add();
		await harness.SessionTools.OpenAsync(session, Docker).WaitAsync(Patience, Ct);
		List<ToolWindowTarget> closedByUser = [];
		harness.Windows.ClosedByUser += closedByUser.Add;

		harness.SessionTools.ReturnToDialog(session, Docker);

		ToolWindowTarget target = ToolWindowTarget.SessionTool(session.Id, "docker");
		Assert.Equal(Docker, harness.SessionTools.DialogFor(session));
		Assert.False(harness.SessionTools.IsInWindow(session, Docker));
		Assert.Equal([session.Id], harness.Tabs.Shown);
		Assert.Equal([target], closedByUser);
	}

	[Fact]
	public async Task ClosingTheSession_ClosesItsWindows()
	{
		FakeToolWindows windows = new();
		await using ToolHarness harness = await CreateAsync(windows);
		FakeSession session = harness.Sessions.Add();
		await harness.SessionTools.OpenAsync(session, Docker).WaitAsync(Patience, Ct);

		harness.Sessions.Remove(session.Id);

		Assert.Contains(session.Id, windows.ClosedSessions);
	}

	[Fact]
	public async Task LockingTheVault_ClosesEveryWindowThisWindowOpened_AndItsDialogs()
	{
		FakeToolWindows windows = new();
		await using ToolHarness harness = await CreateAsync(windows, new AppearanceSettings { ToolsInOwnWindow = false });
		FakeSession session = harness.Sessions.Add();
		await harness.SessionTools.OpenAsync(session, Docker).WaitAsync(Patience, Ct);

		harness.Vault.Lock();

		Assert.Contains(harness.Services, windows.ClosedOwners);
		Assert.Null(harness.SessionTools.DialogFor(session));
	}

	[Fact]
	public async Task AQuestionThisWindowAsks_BringsItInFrontOfItsToolWindows_OncePerQuestion()
	{
		FakeToolWindows windows = new();
		await using ToolHarness harness = await CreateAsync(windows);
		harness.Sessions.Add();

		Task<ConfirmResult> question = harness.Prompts.ConfirmAsync(new ConfirmPrompt { Title = "Sudo", Message = "Go on?" }, Ct);
		harness.Prompts.Notify(NoticeSeverity.Info, "Something else changed the queue.");

		Assert.Equal(1, windows.OwnerShown);
		harness.Prompts.Dispose();
		await question.WaitAsync(Patience, Ct);
	}

	[Fact]
	public async Task AToolThatNeedsTheConnection_SaysSo_InsteadOfOpening()
	{
		FakeToolWindows windows = new();
		await using ToolHarness harness = await CreateAsync(windows);
		FakeSession session = harness.Sessions.Add().Become(SessionState.Disconnected);

		await harness.SessionTools.OpenAsync(session, Docker).WaitAsync(Patience, Ct);

		Assert.Empty(windows.Opened);
		Assert.Null(harness.SessionTools.DialogFor(session));
		Assert.Equal("Docker needs the session to be connected.", Assert.Single(harness.Prompts.TakeNotices()).Message);
	}

	private static Task<ToolHarness> CreateAsync(FakeToolWindows? windows, AppearanceSettings? appearance = null) =>
		ToolHarness.CreateAsync(windows, appearance, sessionTools: [Docker]);

	/// <summary>A tool's component; these tests never render it.</summary>
	private sealed class NoView : SessionViewBase;
}
