using Microsoft.UI.Xaml;
using Mokaterm.Maui.Platforms.Windows;
using Mokaterm.Maui.Services;

namespace Mokaterm.Maui.WinUI;

public partial class App : MauiWinUIApplication
{
	public App()
	{
		// Program.Main installed the crash log before this. Exceptions on the UI thread arrive here instead of at the
		// AppDomain handler. Handled stays false, so the app still ends as it would have without the log.
		UnhandledException += (_, e) => CrashLog.Write("Microsoft.UI.Xaml.Application.UnhandledException", e.Exception, !e.Handled);

		WebView2Setup.PrepareEnvironment();
		InitializeComponent();
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
