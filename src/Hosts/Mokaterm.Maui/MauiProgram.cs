using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using Microsoft.Maui.Platform;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Core.Extensions;
using Mokaterm.Maui.Services;
using Mokaterm.Modules.Ftp;
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
		builder.Services.AddScoped<ILocalFileAccess, MauiLocalFileAccess>();

		// Serial belongs to a host that runs on the machine the ports are plugged into, which is this one.
		builder.Services.AddMokaterm(modules => modules.AddSsh().AddFtp().AddVnc().AddRdp().AddTelnet().AddMqtt().AddSerial());
		builder.Services.AddMokatermSessions();
		builder.Services.AddMokatermUI();

		AddPlatformServices(builder);

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}

	private static void AddPlatformServices(MauiAppBuilder builder)
	{
#if WINDOWS
		builder.Services.AddSingleton<Abstractions.Security.IDeviceKeyProtector, Platforms.Windows.DpapiDeviceKeyProtector>();

		// Replaces the browser drop bridge: WebView2 hands us real paths, so uploads read straight from disk
		// and dropped folders keep their structure.
		builder.Services.AddScoped<IFileDropBridge, Platforms.Windows.WebView2FileDropBridge>();

		// Updates, and the page that drives them, belong to a host that can replace its own files: the web host must
		// show neither, so the descriptor is registered here and not in AddMokatermSettingsPages.
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

		// One window, so one chrome: the shell's top bar becomes that window's title bar.
		builder.Services.AddSingleton<Platforms.Windows.WindowChrome>();
		builder.Services.AddSingleton<IWindowChrome>(services => services.GetRequiredService<Platforms.Windows.WindowChrome>());
		builder.ConfigureLifecycleEvents(events => events.AddWindows(windows => windows.OnWindowCreated(window =>
			window.GetWindow()?.Handler?.MauiContext?.Services.GetService<Platforms.Windows.WindowChrome>()?.Attach(window))));
#endif
	}
}
