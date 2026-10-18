using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Maui.Services;
using Mokaterm.Maui.ToolWindows;
using Mokaterm.UI.Common.Platform;
using MauiApplication = Microsoft.Maui.Controls.Application;
using WinUIWindow = Microsoft.UI.Xaml.Window;

namespace Mokaterm.Maui.Platforms.Windows;

/// <summary>
/// <see cref="ISystemNotifications"/> through the Windows App SDK's app notifications, which this process registered for
/// in <c>Program.Main</c> (<see cref="AppNotificationRegistration"/>). A click brings the main window forward, restored if
/// it was minimized, and hands the ids it carries to the page (<see cref="NotificationClicks"/>), which selects the tab.
/// </summary>
internal sealed class WindowsNotifications : ISystemNotifications, IDisposable
{
	private readonly NotificationClicks _clicks;
	private readonly ILogger<WindowsNotifications> _logger;
	private nint _window;
	private int _failuresLogged;

	public WindowsNotifications(NotificationClicks clicks, ILogger<WindowsNotifications> logger)
	{
		_clicks = clicks;
		_logger = logger;
		AppNotificationRegistration.OnClicked(OnClicked);
		if (AppNotificationRegistration.Failure is { } failure)
		{
			_logger.LogWarning("Windows notifications are off for this run, so trigger notices stay in the window ({Failure}).", LogSafe.Describe(failure));
		}
	}

	public bool IsSessionWindowInForeground
	{
		get
		{
			// Before the window exists there is nothing to be behind, and the toast is all there is.
			nint window = Volatile.Read(ref _window);
			return window == 0 || (GetForegroundWindow() == window && !IsIconic(window));
		}
	}

	/// <summary>The main window, which holds the session tabs. Its tool windows are not it: from OnWindowCreated.</summary>
	public void Attach(WinUIWindow window)
	{
		ArgumentNullException.ThrowIfNull(window);
		Volatile.Write(ref _window, WinRT.Interop.WindowNative.GetWindowHandle(window));
	}

	public bool Show(SystemNotification notification)
	{
		ArgumentNullException.ThrowIfNull(notification);
		if (!AppNotificationRegistration.IsRegistered)
		{
			return false;
		}

		try
		{
			// Turned off for this app or for every app in Windows' settings, or by policy: Show would quietly do nothing.
			AppNotificationManager manager = AppNotificationManager.Default;
			if (manager.Setting != AppNotificationSetting.Enabled)
			{
				return false;
			}

			AppNotificationBuilder builder = new();
			foreach ((string key, string value) in NotificationClick.ArgumentsFor(notification))
			{
				_ = builder.AddArgument(key, value);
			}

			_ = builder.AddText(notification.Title).AddText(notification.Message);
			if (!string.IsNullOrEmpty(notification.Detail))
			{
				_ = builder.AddText(notification.Detail);
			}

			// A session does not outlive a restart of the machine, so neither does a notification about one.
			AppNotification shown = builder.BuildNotification();
			shown.ExpiresOnReboot = true;
			manager.Show(shown);
			return true;
		}
		catch (Exception ex)
		{
			// Said once: a notification that cannot be shown now will not be shown on the next match either. Release builds
			// have no log, so the crash log keeps its type and HRESULT too.
			if (Interlocked.Increment(ref _failuresLogged) == 1)
			{
				_logger.LogWarning("A Windows notification could not be shown; trigger notices stay in the window ({Failure}).", LogSafe.Describe(ex));
				CrashLog.Write("AppNotificationManager.Show", ex, terminating: false);
			}

			return false;
		}
	}

	public void Dispose() => AppNotificationRegistration.OnClicked(null);

	// On a thread pool thread. The window comes forward on the UI thread, where Windows lets the activated app take the
	// foreground, and the page then selects the tab on its own dispatcher.
	private bool OnClicked(NotificationClick click) => MainThread.IsMainThread ? Front(click) : TryEnqueue(click);

	private bool TryEnqueue(NotificationClick click)
	{
		try
		{
			MainThread.BeginInvokeOnMainThread(() => Front(click));
			return true;
		}
		catch (Exception ex)
		{
			_logger.LogWarning("A notification click could not reach the window ({Failure}).", LogSafe.Describe(ex));
			return false;
		}
	}

	private bool Front(NotificationClick click)
	{
		if (MauiApplication.Current?.Windows.FirstOrDefault(window => window is not ToolWindow) is { } main)
		{
			WindowFront.Bring(main);
		}

		_clicks.Deliver(click);
		return true;
	}

	[DllImport("user32.dll", ExactSpelling = true)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	private static extern nint GetForegroundWindow();

	[DllImport("user32.dll", ExactSpelling = true)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool IsIconic(nint window);
}
