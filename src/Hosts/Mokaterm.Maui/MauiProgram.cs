using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using Microsoft.Maui.Platform;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Core.Extensions;
using Mokaterm.Core.Plugins;
using Mokaterm.Core.Plugins.Gallery;
using Mokaterm.Maui.Services;
using Mokaterm.Maui.ToolWindows;
using Mokaterm.Modules.Ftp;
using Mokaterm.Modules.Local;
using Mokaterm.Modules.Mqtt;
using Mokaterm.Modules.Rdp;
using Mokaterm.Modules.Serial;
using Mokaterm.Modules.Ssh;
using Mokaterm.Modules.Telnet;
using Mokaterm.Modules.Vnc;
using Mokaterm.Sessions.Extensions;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Icons;
using Mokaterm.UI.Common.Platform;
using Mokaterm.UI.Extensions;
using Mokaterm.UI.Settings.Pages.Updates;

namespace Mokaterm.Maui;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		MauiAppBuilder builder = MauiApp.CreateBuilder();
		builder.UseMauiApp<App>();

		builder.Services.AddMauiBlazorWebView();

		builder.Services.AddSingleton<IAppEnvironment, MauiAppEnvironment>();
		builder.Services.AddSingleton<DesktopDropCoordinator>();
		builder.Services.AddSingleton<DesktopSessions>();
		builder.Services.AddSingleton<NotificationClicks>();
		builder.Services.AddScoped<ILocalFileAccess, MauiLocalFileAccess>();

		// Tools in windows of their own. Each window's page borrows the services of the window that opened it, which the
		// manager works out from the finished registrations below, and its BlazorWebView needs the handler that hands
		// Blazor that borrowed scope.
		DesktopToolWindows toolWindows = new();
		builder.Services.AddSingleton(toolWindows);
		builder.Services.AddSingleton<IToolWindows>(toolWindows);
		builder.ConfigureMauiHandlers(handlers => handlers.AddHandler<ToolWebView, ToolWebViewHandler>());

		// Serial and the local shell belong to a host that runs on the machine they reach, which is this one.
		// Plugins load before the container is built: a plugin registers services like any module.
		PluginCatalog plugins = PluginCatalog.Open(new PluginCatalogOptions
		{
			Directory = Path.Combine(DesktopPaths.DataDirectory, "plugins"),
			Host = HostKind.Desktop,
			UserManaged = true,
			SkipAll = PluginCatalogOptions.SkipAllRequested(),
		});
		builder.Services.AddMokaterm(modules => modules.AddSsh().AddFtp().AddVnc().AddRdp().AddTelnet().AddMqtt().AddSerial().AddLocal().AddPlugins(plugins));

		// The Plugins page's Browse tab. Nothing is fetched until it opens. MOKATERM_GALLERY_INDEX can point it at another
		// copy of the list, which still needs a production signature.
		builder.Services.AddPluginGallery(PluginGalleryOptions.FromEnvironment());
		builder.Services.AddMokatermSessions();
		builder.Services.AddMokatermUI();

		AddPlatformServices(builder);

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		MauiApp app = builder.Build();
		toolWindows.Prepare(
			builder.Services,
			MokatermUiServiceCollectionExtensions.PageServices,
			app.Services.GetRequiredService<ILogger<DesktopToolWindows>>(),
			ToolWindowChrome());
		return app;
	}

	// Every tool window draws its own title bar like the main window, through a chrome of its own: the main window's
	// is bound to the main window and would move that one's drag regions.
	private static Func<IServiceProvider, IWindowChrome>? ToolWindowChrome()
	{
#if WINDOWS
		return services => new Platforms.Windows.WindowChrome(services.GetRequiredService<Abstractions.Settings.ISettingsService>());
#else
		return null;
#endif
	}

	private static void AddPlatformServices(MauiAppBuilder builder)
	{
#if WINDOWS
		builder.Services.AddSingleton<Abstractions.Security.IDeviceKeyProtector, Platforms.Windows.DpapiDeviceKeyProtector>();

		// Security key logins and key creation. Only this host has one: the key is plugged into the machine the app runs on.
		builder.Services.AddSingleton<Abstractions.Security.ISecurityKeyAuthenticator, Platforms.Windows.WindowsSecurityKeyAuthenticator>();

		// Replaces the browser drop bridge: the window's own drop target hands us real paths, so uploads read
		// straight from disk and dropped folders keep their structure.
		builder.Services.AddScoped<IFileDropBridge, Platforms.Windows.WebView2FileDropBridge>();

		// And that target itself, which is what makes an external drop arrive at all: WinUI refuses one before any
		// drag event reaches the WebView's content (microsoft-ui-xaml#10142). One window, so one target.
		builder.Services.AddSingleton<Platforms.Windows.NativeFileDrop>();

#if !MOKATERM_PACKAGED
		// Updates, and the page that drives them, belong to a host that can replace its own files: the web host must
		// show neither, so the descriptor is registered here and not in AddMokatermSettingsPages. A Store package is
		// read-only and updates through the Store, so that build leaves both out as well.
		builder.Services.AddSingleton<IAppUpdater, Platforms.Windows.VelopackAppUpdater>();
		builder.Services.AddSettingsPage(new SettingsPageDescriptor
		{
			Id = "updates",
			Title = "Updates",
			Description = "Where Mokaterm looks for new versions, and installing one. Nothing is downloaded or restarted without you.",
			Icon = MokatermIcons.Download,
			Component = typeof(UpdateSettingsPage),
			Group = SettingsPageGroup.Application,
			Order = 40,
			Keywords = "update updates upgrade version release feed channel velopack download install restart startup check",
		});
#endif

		// Plugin changes apply at the next start, which the Plugins page can start from here.
		builder.Services.AddSingleton<IAppRestarter, Platforms.Windows.DesktopRestarter>();

		// Trigger notices as Windows notifications while the window is out of sight. Program.Main registered the process
		// before this runs; when that failed the contract stays out, so the Triggers page offers nothing that cannot work.
		builder.Services.AddSingleton<Platforms.Windows.WindowsNotifications>();
		if (Platforms.Windows.AppNotificationRegistration.IsRegistered)
		{
			builder.Services.AddSingleton<ISystemNotifications>(services => services.GetRequiredService<Platforms.Windows.WindowsNotifications>());
		}

		// A copied secret is cleared through Windows when no page can: at shutdown, or while the page waits for focus.
		builder.Services.AddSingleton<ISystemClipboard, Platforms.Windows.WindowsSystemClipboard>();

		// The main window's chrome: the shell's top bar becomes its title bar. Tool windows each make their own
		// (ToolWindowChrome above), which their pages find in place of this one.
		builder.Services.AddSingleton<Platforms.Windows.WindowChrome>();
		builder.Services.AddSingleton<IWindowChrome>(services => services.GetRequiredService<Platforms.Windows.WindowChrome>());

		// Both of these need the window itself, and its handler is connected by the time this runs.
		builder.ConfigureLifecycleEvents(events => events.AddWindows(windows => windows.OnWindowCreated(window =>
		{
			// A tool window has a chrome of its own and takes no file drops: both singletons serve the main window, and
			// attaching either to a tool window would take it from the main one.
			if (window.GetWindow() is ToolWindow tool)
			{
				(tool.Chrome as Platforms.Windows.WindowChrome)?.Attach(window);
				DesktopToolWindows.ApplyKeepOnTop(tool);
				return;
			}

			IServiceProvider? services = window.GetWindow()?.Handler?.MauiContext?.Services;
			services?.GetService<Platforms.Windows.WindowChrome>()?.Attach(window);
			services?.GetService<Platforms.Windows.NativeFileDrop>()?.Attach(window);

			// Created here rather than on first use, so a click on a notification has somewhere to go from the start.
			services?.GetService<Platforms.Windows.WindowsNotifications>()?.Attach(window);
		})));
#endif
	}
}
