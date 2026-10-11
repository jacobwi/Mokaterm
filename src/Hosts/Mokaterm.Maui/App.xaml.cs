using Mokaterm.Maui.Services;
using Mokaterm.Maui.ToolWindows;

namespace Mokaterm.Maui;

public partial class App : Application
{
	private readonly DesktopSessions _sessions;
	private readonly DesktopToolWindows _toolWindows;

	public App(DesktopSessions sessions, DesktopToolWindows toolWindows)
	{
		_sessions = sessions;
		_toolWindows = toolWindows;
		InitializeComponent();

		// The UI is dark-only by default; keep native parts (scrollbars, pickers) consistent with it. The window's own
		// buttons follow the Moka theme instead (Platforms/Windows/WindowChrome).
		UserAppTheme = AppTheme.Dark;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		// No TitleBar here: on Windows the page's own top bar is the title bar (Platforms/Windows/WindowChrome).
		Window window = new(new MainPage())
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
	// own deadline, so the wait cannot outlast it. Tool windows go first: they borrow this window's services, and the
	// app would otherwise keep running in them after its main window is gone.
	private void OnWindowDestroying(object? sender, EventArgs e)
	{
		_toolWindows.CloseEverything();
		_sessions.ShutDownAsync().Wait();
	}
}
