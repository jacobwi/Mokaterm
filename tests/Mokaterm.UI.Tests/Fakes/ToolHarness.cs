using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Platform;
using Mokaterm.UI.Interaction;
using Mokaterm.UI.Shell;
using Mokaterm.UI.Tools;
using Mokaterm.UI.Workspace;

namespace Mokaterm.UI.Tests.Fakes;

/// <summary>
/// The shell's tool services over one window's state, as the container builds them: the window rule
/// (<see cref="ShellToolWindows"/>), session and app tools, and the tool window restore beside the session restore.
/// Without a window host it is the web host, where every tool stays in its dialog.
/// </summary>
internal sealed class ToolHarness : IAsyncDisposable
{
	private ToolHarness(
		FakeToolWindows? windows,
		AppearanceSettings appearance,
		SessionRestoreMode restoreMode,
		MemoryAppDataStore documents,
		UiStateStore uiState,
		IEnumerable<SessionToolDescriptor> sessionTools,
		IEnumerable<AppToolDescriptor> appTools)
	{
		Host = windows;
		Documents = documents;
		UiState = uiState;
		Settings = new FixedSettings(appearance, new GeneralSettings { RestoreSessions = restoreMode });
		ServiceCollection services = new();
		if (windows is not null)
		{
			services.AddSingleton<IToolWindows>(windows);
		}

		Services = services.BuildServiceProvider();
		Contributions = new UiContributions([], [], sessionTools, appTools: appTools);
		Windows = new ShellToolWindows(Settings, UiState, Prompts, Vault, Services, NullLogger<ShellToolWindows>.Instance);
		SessionTools = new SessionToolActions(Contributions, Sessions, Vault, Windows, Tabs, Prompts);
		AppTools = new AppToolActions(Contributions, Vault, Windows);
		Workspace = new SessionWorkspace(Sessions);
		Restore = new SessionRestore(Sessions, Workspace, UiState, Settings, Prompts, NullLogger<SessionRestore>.Instance);
		ToolRestore = new ToolWindowRestore(Windows, SessionTools, AppTools, Restore, Sessions, UiState, Settings);
	}

	/// <summary>The window host, or null for a host without windows.</summary>
	public FakeToolWindows? Host { get; }

	public ManualSessionManager Sessions { get; } = new();

	public LockableVault Vault { get; } = new();

	public UserInteractionService Prompts { get; } = new();

	public RecordingTabs Tabs { get; } = new();

	public FixedSettings Settings { get; }

	/// <summary>Where the ui-state document is written.</summary>
	public MemoryAppDataStore Documents { get; }

	public UiStateStore UiState { get; }

	public ServiceProvider Services { get; }

	public UiContributions Contributions { get; }

	public ShellToolWindows Windows { get; }

	public SessionToolActions SessionTools { get; }

	public AppToolActions AppTools { get; }

	public SessionWorkspace Workspace { get; }

	public SessionRestore Restore { get; }

	public ToolWindowRestore ToolRestore { get; }

	/// <param name="windows">The window host, or null for one without windows.</param>
	/// <param name="appearance">The appearance settings, where "Open tools in their own window" is on by default.</param>
	/// <param name="restoreMode">Whether sessions come back at start.</param>
	/// <param name="saved">What the last run left in the ui-state document, if anything.</param>
	/// <param name="sessionTools">The session tools modules registered.</param>
	/// <param name="appTools">The app tools modules registered.</param>
	public static async Task<ToolHarness> CreateAsync(
		FakeToolWindows? windows,
		AppearanceSettings? appearance = null,
		SessionRestoreMode restoreMode = SessionRestoreMode.Always,
		UiStateDocument? saved = null,
		IEnumerable<SessionToolDescriptor>? sessionTools = null,
		IEnumerable<AppToolDescriptor>? appTools = null)
	{
		MemoryAppDataStore documents = new();
		if (saved is not null)
		{
			documents.Set("ui-state", saved);
		}

		UiStateStore uiState = new(documents, TimeProvider.System, NullLogger<UiStateStore>.Instance);
		await uiState.LoadAsync(TestContext.Current.CancellationToken);
		return new ToolHarness(
			windows,
			appearance ?? new AppearanceSettings(),
			restoreMode,
			documents,
			uiState,
			sessionTools ?? [],
			appTools ?? []);
	}

	/// <summary>What the shell does once the vault is unlocked: the tool window restore first, then the session restore.</summary>
	public async Task StartAsync()
	{
		Task toolWindows = ToolRestore.StartAsync();
		await Restore.StartAsync();
		await toolWindows;
	}

	/// <summary>The user closes a session's tab, which forgets it before it goes.</summary>
	public void CloseTab(Guid sessionId)
	{
		Restore.Forget(sessionId);
		Sessions.Remove(sessionId);
	}

	public async ValueTask DisposeAsync()
	{
		ToolRestore.Dispose();
		Restore.Dispose();
		Workspace.Dispose();
		AppTools.Dispose();
		SessionTools.Dispose();
		Windows.Dispose();
		Prompts.Dispose();
		await UiState.DisposeAsync();
		await Services.DisposeAsync();
	}

	/// <summary>Records which session's tab was asked to come to the front.</summary>
	internal sealed class RecordingTabs : ISessionTabs
	{
		public List<Guid> Shown { get; } = [];

		public void Show(Guid sessionId) => Shown.Add(sessionId);
	}
}
