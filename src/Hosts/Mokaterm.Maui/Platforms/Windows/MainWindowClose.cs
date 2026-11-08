using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Maui.Platform;
using Microsoft.UI.Windowing;
using Mokaterm.Maui.Services;
using Mokaterm.Maui.ToolWindows;
using WebView2 = Microsoft.UI.Xaml.Controls.WebView2;
using WinUIWindow = Microsoft.UI.Xaml.Window;

namespace Mokaterm.Maui.Platforms.Windows;

/// <summary>
/// Ends the app without freezing its window. Only AppWindow.Closing can still be refused, so the close is refused there
/// once, the sessions end in the background behind a closing screen, and the window then closes for real. Ways out that
/// never pass Closing (a restart, the system ending the session) still end in the window's Destroying handler.
/// </summary>
internal sealed class MainWindowClose
{
	// A close this quick has nothing to report, and a screen shown for a moment would only flash.
	private static readonly TimeSpan ScreenDelay = TimeSpan.FromMilliseconds(150);

	private readonly DesktopSessions _sessions;
	private readonly DesktopToolWindows _toolWindows;
	private WinUIWindow? _window;
	private bool _closing;
	private bool _finished;

	public MainWindowClose(DesktopSessions sessions, DesktopToolWindows toolWindows)
	{
		_sessions = sessions;
		_toolWindows = toolWindows;
	}

	public void Attach(WinUIWindow native)
	{
		_window = native;
		native.AppWindow.Closing += OnClosing;
	}

	private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
	{
		if (_finished)
		{
			return;
		}

		args.Cancel = true;
		if (!_closing)
		{
			_closing = true;
			_ = CloseAsync();
		}
	}

	private async Task CloseAsync()
	{
		try
		{
			_toolWindows.CloseEverything();
			Task shutdown = _sessions.ShutDownOnceAsync();
			if (await Task.WhenAny(shutdown, Task.Delay(ScreenDelay)) != shutdown)
			{
				ShowClosingScreen();
			}

			await shutdown;
		}
		catch (Exception ex)
		{
			CrashLog.Write("Window close", ex, terminating: false);
		}
		finally
		{
			_finished = true;
			_window?.Close();
		}
	}

	private void ShowClosingScreen()
	{
		if (_window?.GetWindow() is not Microsoft.Maui.Controls.Window window)
		{
			return;
		}

		BlazorWebView? view = window.Page?.GetVisualTreeDescendants().OfType<BlazorWebView>().FirstOrDefault();
		try
		{
			_ = (view?.Handler?.PlatformView as WebView2)?.CoreWebView2?.ExecuteScriptAsync("document.getElementById('mt-closing')?.showPopover()");
		}
		catch (COMException)
		{
			// The page is already going; the window closes all the same.
		}
	}
}
