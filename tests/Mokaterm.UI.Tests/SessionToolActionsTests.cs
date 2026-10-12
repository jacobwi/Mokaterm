using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Components;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Icons;
using Mokaterm.UI.Common.Platform;
using Mokaterm.UI.Interaction;
using Mokaterm.UI.Shell;
using Mokaterm.UI.Tests.Fakes;
using Mokaterm.UI.Workspace;

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
		await using Harness harness = new(windows: null);
		FakeSession session = harness.Sessions.Add();

		await harness.Tools.ToggleAsync(session, Docker).WaitAsync(Patience, Ct);
		Assert.Equal(Docker, harness.Tools.DialogFor(session));
		Assert.True(harness.Tools.IsOpen(session, Docker));

		await harness.Tools.ToggleAsync(session, Docker).WaitAsync(Patience, Ct);
		Assert.Null(harness.Tools.DialogFor(session));
	}

	[Fact]
	public async Task WindowHost_SettingOn_OpensAWindowTitledWithTheToolAndTheSession()
	{
		FakeToolWindows windows = new();
		await using Harness harness = new(windows);
		FakeSession session = harness.Sessions.Add();

		await harness.Tools.OpenAsync(session, Docker).WaitAsync(Patience, Ct);

		ToolWindowRequest request = Assert.Single(windows.Opened);
		Assert.Equal("Docker · bc@build-01.example.com", request.Title);
		Assert.Equal(session.Id, request.SessionId);
		Assert.Equal("docker", request.ToolKey);
		Assert.Equal(SessionToolSize.Wide, request.Size);
		Assert.Same(harness.Services, request.Services);
		Assert.Null(harness.Tools.DialogFor(session));
	}

	[Fact]
	public async Task WindowHost_SettingOff_KeepsTheDialog()
	{
		FakeToolWindows windows = new();
		await using Harness harness = new(windows, new AppearanceSettings { ToolsInOwnWindow = false });
		FakeSession session = harness.Sessions.Add();

		await harness.Tools.OpenAsync(session, Docker).WaitAsync(Patience, Ct);

		Assert.Empty(windows.Opened);
		Assert.Equal(Docker, harness.Tools.DialogFor(session));
	}

	[Fact]
	public async Task AWindowThatWillNotOpen_FallsBackToTheDialog()
	{
		FakeToolWindows windows = new() { Opens = false };
		await using Harness harness = new(windows);
		FakeSession session = harness.Sessions.Add();

		await harness.Tools.OpenAsync(session, Docker).WaitAsync(Patience, Ct);

		Assert.Equal(Docker, harness.Tools.DialogFor(session));
	}

	[Fact]
	public async Task PopOut_MovesTheDialogToAWindow()
	{
		FakeToolWindows windows = new();
		await using Harness harness = new(windows, new AppearanceSettings { ToolsInOwnWindow = false });
		FakeSession session = harness.Sessions.Add();
		await harness.Tools.OpenAsync(session, Docker).WaitAsync(Patience, Ct);

		await harness.Tools.PopOutAsync(session, Docker).WaitAsync(Patience, Ct);

		Assert.Single(windows.Opened);
		Assert.Null(harness.Tools.DialogFor(session));
		Assert.True(harness.Tools.IsInWindow(session, Docker));
	}

	[Fact]
	public async Task ClosingTheSession_ClosesItsWindows()
	{
		FakeToolWindows windows = new();
		await using Harness harness = new(windows);
		FakeSession session = harness.Sessions.Add();
		await harness.Tools.OpenAsync(session, Docker).WaitAsync(Patience, Ct);

		harness.Sessions.Remove(session.Id);

		Assert.Contains(session.Id, windows.ClosedSessions);
	}

	[Fact]
	public async Task LockingTheVault_ClosesEveryWindowThisWindowOpened_AndItsDialogs()
	{
		FakeToolWindows windows = new();
		await using Harness harness = new(windows, new AppearanceSettings { ToolsInOwnWindow = false });
		FakeSession session = harness.Sessions.Add();
		await harness.Tools.OpenAsync(session, Docker).WaitAsync(Patience, Ct);

		harness.Vault.Lock();

		Assert.Contains(harness.Services, windows.ClosedOwners);
		Assert.Null(harness.Tools.DialogFor(session));
	}

	[Fact]
	public async Task AQuestionThisWindowAsks_BringsItInFrontOfItsToolWindows_OncePerQuestion()
	{
		FakeToolWindows windows = new();
		await using Harness harness = new(windows);
		harness.Sessions.Add();

		Task<ConfirmResult> question = harness.Prompts.ConfirmAsync(new ConfirmPrompt { Title = "Sudo", Message = "Go on?" }, Ct);
		harness.Prompts.Notify(NoticeSeverity.Info, "Something else changed the queue.");

		Assert.Equal(1, windows.OwnerShown);
		harness.Prompts.Dispose();
		await question.WaitAsync(Patience, Ct);
	}

	[Fact]
	public async Task AWindowsPlace_IsRememberedPerTool_AndOffered_ToTheNextOne()
	{
		FakeToolWindows windows = new();
		await using Harness harness = new(windows);
		FakeSession session = harness.Sessions.Add();
		await harness.Tools.OpenAsync(session, Docker).WaitAsync(Patience, Ct);

		ToolWindowBounds moved = new(40, 60, 1000, 700);
		windows.Opened[0].BoundsChanged!(moved);
		windows.Shown.Clear();
		await harness.Tools.OpenAsync(harness.Sessions.Add(), Docker).WaitAsync(Patience, Ct);

		Assert.Equal(moved, harness.UiState.Current.ToolWindows["docker"]);
		Assert.Equal(moved, windows.Opened[1].Bounds);
	}

	private sealed class Harness : IAsyncDisposable
	{
		public Harness(FakeToolWindows? windows, AppearanceSettings? appearance = null)
		{
			ServiceCollection services = new();
			if (windows is not null)
			{
				services.AddSingleton<IToolWindows>(windows);
			}

			Services = services.BuildServiceProvider();
			UiState = new UiStateStore(new MemoryAppDataStore(), TimeProvider.System, NullLogger<UiStateStore>.Instance);
			Tools = new SessionToolActions(
				new UiContributions([], [], [Docker]),
				Sessions,
				Vault,
				new FixedSettings(appearance ?? new AppearanceSettings()),
				UiState,
				Prompts,
				Services,
				NullLogger<SessionToolActions>.Instance);
		}

		public ManualSessionManager Sessions { get; } = new();

		public LockableVault Vault { get; } = new();

		public UserInteractionService Prompts { get; } = new();

		public UiStateStore UiState { get; }

		public ServiceProvider Services { get; }

		public SessionToolActions Tools { get; }

		public async ValueTask DisposeAsync()
		{
			Tools.Dispose();
			Prompts.Dispose();
			await UiState.DisposeAsync();
			await Services.DisposeAsync();
		}
	}

	/// <summary>A window host that records what it was asked to do.</summary>
	private sealed class FakeToolWindows : IToolWindows
	{
		public event Action? Changed;

		public bool Opens { get; init; } = true;

		public List<ToolWindowRequest> Opened { get; } = [];

		public List<ToolWindowRequest> Shown { get; } = [];

		public List<Guid> ClosedSessions { get; } = [];

		public List<IServiceProvider> ClosedOwners { get; } = [];

		public int OwnerShown { get; private set; }

		public bool IsOpen(Guid sessionId, string toolKey) =>
			Shown.Exists(request => request.SessionId == sessionId && request.ToolKey == toolKey);

		public Task<bool> OpenAsync(ToolWindowRequest request)
		{
			if (!Opens)
			{
				return Task.FromResult(false);
			}

			Opened.Add(request);
			Shown.Add(request);
			Changed?.Invoke();
			return Task.FromResult(true);
		}

		public void Close(Guid sessionId, string? toolKey = null) => ClosedSessions.Add(sessionId);

		public void CloseAll(IServiceProvider services) => ClosedOwners.Add(services);

		public void ShowOwner(IServiceProvider services) => OwnerShown++;
	}

	/// <summary>A tool's component; these tests never render it.</summary>
	private sealed class NoView : SessionViewBase;
}
