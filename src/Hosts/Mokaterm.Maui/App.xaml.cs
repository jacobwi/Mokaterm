using Mokaterm.Maui.Services;

namespace Mokaterm.Maui;

public partial class App : Application
{
	private readonly DesktopDropCoordinator _dropCoordinator;
	private readonly DesktopSessions _sessions;

	public App(DesktopDropCoordinator dropCoordinator, DesktopSessions sessions)
	{
		_dropCoordinator = dropCoordinator;
		_sessions = sessions;
		InitializeComponent();

		// The UI is dark-only by default; keep native parts (scrollbars, pickers) consistent with it. The window's own
		// buttons follow the Moka theme instead (Platforms/Windows/WindowChrome).
		UserAppTheme = AppTheme.Dark;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		// No TitleBar here: on Windows the page's own top bar is the title bar (Platforms/Windows/WindowChrome).
		Window window = new(new MainPage(_dropCoordinator))
		{
			Title = "Mokaterm",
			MinimumWidth = 960,
			MinimumHeight = 600,
		};

		window.Destroying += OnWindowDestroying;
		return window;
	}

	// Blocks the UI thread on purpose, because the page's sessions are otherwise only closed by a disposal the exiting
	// process does not wait for (see DesktopSessions). Nothing of that work needs this thread back, and it carries its
	// own deadline, so the wait cannot outlast it.
	private void OnWindowDestroying(object? sender, EventArgs e) => _sessions.ShutDownAsync().Wait();
}
