using Microsoft.Windows.AppNotifications;
using Mokaterm.Maui.Services;

namespace Mokaterm.Maui.Platforms.Windows;

/// <summary>
/// This process's registration for Windows app notifications. Made from <c>Program.Main</c>, before the app's container
/// exists, because a click on a notification can be what started the process: Windows then waits for the class the
/// click activates, and the Windows App SDK hands the click's arguments to <c>AppInstance.GetActivatedEventArgs</c> only
/// once the process registered. Undone when the main window goes. A failure is kept for the log and leaves the app with
/// toasts only.
/// </summary>
internal static class AppNotificationRegistration
{
	/// <summary>What Windows starts the app with for a click on one of its notifications (the SDK's own marker).</summary>
	public const string ActivationArgument = "----AppNotificationActivated:";

	private const string DisplayName = "Mokaterm";

	// Copied next to the exe by the project file. Without it the SDK extracts the exe's 16 px icon into a file of its own.
	private const string IconFile = "mokaterm.ico";

	private static readonly Lock Gate = new();
	private static Func<NotificationClick, bool>? _clicked;
	private static bool _tried;
	private static bool _registered;

	public static bool IsRegistered
	{
		get
		{
			lock (Gate)
			{
				return _registered;
			}
		}
	}

	/// <summary>Why registering failed, for the crash log and the app's log; null when it did not.</summary>
	public static Exception? Failure { get; private set; }

	public static bool StartedByNotification(IEnumerable<string> arguments) =>
		arguments.Any(argument => argument.StartsWith(ActivationArgument, StringComparison.Ordinal));

	/// <summary>Registers this process once, on the thread WinUI will run on. Never throws.</summary>
	public static void Register()
	{
		lock (Gate)
		{
			if (_tried)
			{
				return;
			}

			_tried = true;
		}

		// An elevated process gets no notifications from Windows at all, and the SDK's Register would quietly do nothing.
		AppNotificationManager? manager = null;
		try
		{
			if (!AppNotificationManager.IsSupported())
			{
				return;
			}

			manager = AppNotificationManager.Default;

			// Before Register, which then takes every later click in this process instead of starting a new one.
			manager.NotificationInvoked += OnInvoked;
			RegisterApp(manager);
			lock (Gate)
			{
				_registered = true;
			}
		}
		catch (Exception ex)
		{
			Failure = ex;
			try
			{
				if (manager is not null)
				{
					manager.NotificationInvoked -= OnInvoked;
				}
			}
			catch (Exception)
			{
				// The handler only ever forwards a click, and without a registration no click comes.
			}
		}
	}

	/// <summary>Undoes <see cref="Register"/>, as the SDK asks before the process ends. Never throws.</summary>
	public static void Unregister()
	{
		lock (Gate)
		{
			if (!_registered)
			{
				return;
			}

			_registered = false;
		}

		try
		{
			AppNotificationManager.Default.Unregister();
		}
		catch (Exception)
		{
			// The process is ending, and its class registration ends with it.
		}
	}

	/// <summary>
	/// Removes everything <see cref="Register"/> left with Windows for this exe: its app id, the activator class that
	/// points at the exe, the icon path. For the uninstaller, whose process ends right after; never throws.
	/// </summary>
	public static void RemoveForUninstall()
	{
		try
		{
			// The hook runs before Program.Main sets the WinRT wrappers up, and the process ends after it.
			WinRT.ComWrappersSupport.InitializeComWrappers();
			AppNotificationManager.Default.UnregisterAll();
		}
		catch (Exception)
		{
			// Never registered, or Windows refused: the leftover entries only name a file that is about to go.
		}
	}

	/// <summary>
	/// Where clicks go once the app is up: the handler answers whether it took the click. Before there is one, a click
	/// finds the window coming up anyway.
	/// </summary>
	public static void OnClicked(Func<NotificationClick, bool>? handler)
	{
		lock (Gate)
		{
			_clicked = handler;
		}
	}

	/// <summary>
	/// A click, from the SDK's event or handed over by a second launch (<see cref="SingleInstance"/>). False when nothing
	/// took it, so the caller can at least bring the window forward.
	/// </summary>
	public static bool Deliver(IEnumerable<KeyValuePair<string, string>>? arguments)
	{
		Func<NotificationClick, bool>? handler;
		lock (Gate)
		{
			handler = _clicked;
		}

		return handler?.Invoke(NotificationClick.Read(arguments)) == true;
	}

	private static void RegisterApp(AppNotificationManager manager)
	{
#if MOKATERM_PACKAGED
		// The package manifest names the class a click activates (windows.toastNotificationActivation), and the name and
		// icon are the package's; the overload that takes them refuses a packaged process.
		manager.Register();
#else
		// Unpackaged, the SDK would take the name from the best shortcut it finds and otherwise from the exe's file name
		// ("Mokaterm.Maui"), so the name is given, with an icon to match.
		string icon = Path.Combine(AppContext.BaseDirectory, IconFile);
		if (File.Exists(icon))
		{
			try
			{
				manager.Register(DisplayName, new Uri(icon));
				return;
			}
			catch (Exception)
			{
				// The SDK checks the name and the icon before it registers anything, so a refusal leaves the plain
				// registration open, with the shortcut's or the exe's name and icon.
			}
		}

		manager.Register();
#endif
	}

	// Raised on a thread pool thread.
	private static void OnInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args) => _ = Deliver(args.Arguments);
}
