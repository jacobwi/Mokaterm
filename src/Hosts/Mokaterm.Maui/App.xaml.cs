using Mokaterm.Maui.Services;
using Mokaterm.Maui.ToolWindows;

namespace Mokaterm.Maui;

public partial class App : Application
{
	private readonly DesktopSessions _sessions;
	private readonly DesktopToolWindows _toolWindows;
	private readonly DesktopWindowTitle _title;
	private readonly DesktopAppearance _appearance;

	public App(DesktopSessions sessions, DesktopToolWindows toolWindows, DesktopWindowTitle title, DesktopAppearance appearance)
	{
		_sessions = sessions;
		_toolWindows = toolWindows;
		_title = title;
		_appearance = appearance;
		InitializeComponent();

		// Native parts (the window's background before the page, scrollbars, pickers) start in the app's own mode, which
		// is in settings.json, not the system's. A change while running applies at the next start: by then the page
		// covers everything native. The window's own buttons follow the Moka theme (Platforms/Windows/WindowChrome).
		UserAppTheme = appearance.IsLight ? AppTheme.Light : AppTheme.Dark;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		// No TitleBar here: on Windows the page's own top bar is the title bar (Platforms/Windows/WindowChrome).
		Window window = new(new MainPage(_appearance))
		{
			Title = DesktopWindowTitle.AppName,
			MinimumWidth = 960,
			MinimumHeight = 600,
		};

		_title.Attach(window);
		window.Destroying += OnWindowDestroying;
		StartupTrace.Mark("Window created");
		return window;
	}

	// After a close through the window's button MainWindowClose has already done this work and the wait is over at once.
	// Other ways out arrive here first, and block the UI thread on purpose, because the page's sessions are otherwise only
	// closed by a disposal the exiting process does not wait for (see DesktopSessions). Nothing of that work needs this
	// thread back, and it carries its own deadline, so the wait cannot outlast it. Tool windows go first: they borrow this
	// window's services, and the app would otherwise keep running in them after its main window is gone.
	private void OnWindowDestroying(object? sender, EventArgs e)
	{
		_toolWindows.CloseEverything();
		_sessions.ShutDownOnceAsync().Wait();

#if WINDOWS
		// On the thread that registered, as the Windows App SDK asks: a click after this starts the app again.
		Platforms.Windows.AppNotificationRegistration.Unregister();
#endif
	}
}
