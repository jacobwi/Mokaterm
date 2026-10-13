using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.UI.Common.Platform;
using Mokaterm.UI.Interaction;
using Mokaterm.UI.Shell;
using Mokaterm.UI.Tests.Fakes;
using Mokaterm.UI.Workspace;

namespace Mokaterm.UI.Tests;

/// <summary>
/// How a module brings a session it opened to the front: a session the manager adds joins the focused pane without
/// taking it over, and <see cref="ISessionTabs.Show"/> makes it the tab the user sees, from a dialog or a tool window.
/// </summary>
public sealed class SessionTabsTests
{
	[Fact]
	public void ASessionTheManagerOpened_JoinsThePane_ButStaysBehindUntilShown()
	{
		using Harness harness = new();
		FakeSession working = harness.Manager.Add();
		FakeSession opened = harness.Manager.Add();
		Assert.Equal(working.Id, harness.Workspace.ActiveId);

		harness.Tabs.Show(opened.Id);

		Assert.Equal(opened.Id, harness.Workspace.ActiveId);
		Assert.Equal(harness.Workspace.PaneOf(working.Id), harness.Workspace.PaneOf(opened.Id));
	}

	// A module may ask before the manager has told the tabs about the session.
	[Fact]
	public void ShowingASessionBeforeItArrives_ShowsItWhenItDoes()
	{
		using Harness harness = new();
		FakeSession working = harness.Manager.Add();
		FakeSession opened = new();

		harness.Tabs.Show(opened.Id);
		Assert.Equal(working.Id, harness.Workspace.ActiveId);
		harness.Manager.Add(opened);

		Assert.Equal(opened.Id, harness.Workspace.ActiveId);
	}

	[Fact]
	public void ASessionInAnotherPane_TakesTheFocusWithIt()
	{
		using Harness harness = new();
		FakeSession left = harness.Manager.Add();
		FakeSession right = harness.Manager.Add();
		harness.Workspace.MoveToOtherPane(right.Id);
		harness.Workspace.FocusPane(harness.Workspace.PaneOf(left.Id));

		harness.Tabs.Show(right.Id);

		Assert.Equal(harness.Workspace.PaneOf(right.Id), harness.Workspace.FocusedPane);
		Assert.Equal(right.Id, harness.Workspace.ActiveId);
	}

	[Fact]
	public void Showing_ClosesTheSettingsThatWouldCoverTheTab()
	{
		using Harness harness = new();
		FakeSession session = harness.Manager.Add();
		harness.Shell.OpenSettings();

		harness.Tabs.Show(session.Id);

		Assert.False(harness.Shell.SettingsOpen);
	}

	[Fact]
	public void ASessionThatNeverArrives_ChangesNothing()
	{
		using Harness harness = new();
		FakeSession working = harness.Manager.Add();

		harness.Tabs.Show(Guid.NewGuid());
		FakeSession later = harness.Manager.Add();

		Assert.Equal(working.Id, harness.Workspace.ActiveId);
		Assert.NotEqual(later.Id, harness.Workspace.ActiveId);
	}

	// The tool window borrows this window's instance, so the request lands here and this window has to come forward.
	[Fact]
	public void WithToolWindows_TheWindowHoldingTheTabComesForward()
	{
		OwnerRecorder windows = new();
		using Harness harness = new(windows);
		FakeSession session = harness.Manager.Add();

		harness.Tabs.Show(session.Id);

		Assert.Same(harness.Services, Assert.Single(windows.Owners));
	}

	private sealed class Harness : IDisposable
	{
		private readonly UserInteractionService _interaction = new();

		public Harness(IToolWindows? windows = null)
		{
			UiStateStore uiState = new(new MemoryAppDataStore(), TimeProvider.System, NullLogger<UiStateStore>.Instance);
			FixedSettings settings = new();
			Shell = new ShellState(uiState);
			Workspace = new SessionWorkspace(Manager);
			Restore = new SessionRestore(Manager, Workspace, uiState, settings, _interaction, NullLogger<SessionRestore>.Instance);
			SessionActions actions = new(
				Manager,
				Workspace,
				Restore,
				new NoProtocols(),
				_interaction,
				settings,
				Shell,
				NullLogger<SessionActions>.Instance);

			ServiceCollection services = new();
			if (windows is not null)
			{
				services.AddSingleton(windows);
			}

			Services = services.BuildServiceProvider();
			Tabs = new SessionTabs(actions, Services);
		}

		public ManualSessionManager Manager { get; } = new();

		public SessionWorkspace Workspace { get; }

		public SessionRestore Restore { get; }

		public ShellState Shell { get; }

		public ServiceProvider Services { get; }

		public SessionTabs Tabs { get; }

		public void Dispose()
		{
			Restore.Dispose();
			Workspace.Dispose();
			_interaction.Dispose();
			Services.Dispose();
		}
	}

	/// <summary>A window host that only records which window was asked to come forward.</summary>
	private sealed class OwnerRecorder : IToolWindows
	{
		public event Action? Changed
		{
			add { }
			remove { }
		}

		public List<IServiceProvider> Owners { get; } = [];

		public bool IsOpen(ToolWindowTarget target) => false;

		public Task<bool> OpenAsync(ToolWindowRequest request) => Task.FromResult(false);

		public void Close(ToolWindowTarget target)
		{
		}

		public void CloseSession(Guid sessionId)
		{
		}

		public void CloseAll(IServiceProvider services)
		{
		}

		public void ShowOwner(IServiceProvider services) => Owners.Add(services);

		public bool IsKeptOnTop(ToolWindowTarget target) => false;

		public void SetKeepOnTop(ToolWindowTarget target, bool keepOnTop)
		{
		}
	}
}
