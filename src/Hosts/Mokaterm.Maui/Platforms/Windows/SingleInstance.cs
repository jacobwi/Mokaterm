using System.Security.Cryptography;
using System.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.Windows.AppLifecycle;
using MauiApplication = Microsoft.Maui.Controls.Application;
using WinUIWindow = Microsoft.UI.Xaml.Window;

namespace Mokaterm.Maui.Platforms.Windows;

/// <summary>
/// One process per data folder. A second process on the same vault would overwrite the first one's writes and keep its
/// own unlock throttling, so a second launch hands its activation to the running process, which comes to the front,
/// and exits.
/// </summary>
internal static class SingleInstance
{
	// The redirect waits until the running process has picked the request up; a hung one must not keep this one alive.
	private static readonly TimeSpan RedirectTimeout = TimeSpan.FromSeconds(10);

	// Held for the life of the process; Windows releases it when the process ends, crashes included.
	private static Mutex? _folderLock;
	private static DispatcherQueue? _uiQueue;

	/// <summary>
	/// Hands this launch to the process started from this executable that already has <paramref name="dataRoot"/>, which
	/// then comes to the front. True when there was one and this process should exit.
	/// </summary>
	public static bool HandOver(string dataRoot)
	{
		AppInstance owner = AppInstance.FindOrRegisterForKey(KeyFor(dataRoot));
		if (owner.IsCurrent)
		{
			owner.Activated += OnActivated;
			return false;
		}

		AppActivationArguments arguments = AppInstance.GetCurrent().GetActivatedEventArgs();

		// Off this STA thread, as the Windows App SDK sample does; the managed wait below keeps pumping COM meanwhile.
		// RedirectActivationToAsync also passes this process's right to take the foreground on to the running one.
		Task redirect = Task.Run(() => owner.RedirectActivationToAsync(arguments).AsTask());
		try
		{
			_ = redirect.Wait(RedirectTimeout);
		}
		catch (AggregateException)
		{
			// The running process ended during the redirect; the next launch starts normally.
		}

		return true;
	}

	/// <summary>
	/// Holds <paramref name="dataRoot"/> for the rest of this process. False when another copy of the app has it. The
	/// Windows App SDK scopes instance keys to the executable's path, so a portable copy or a development build on the
	/// same folder never meets this process in <see cref="HandOver"/>; a named mutex is shared by every copy.
	/// </summary>
	public static bool LockDataFolder(string dataRoot)
	{
		if (_folderLock is not null)
		{
			return true;
		}

		Mutex folderLock = new(initiallyOwned: false, @"Local\" + KeyFor(dataRoot));
		bool acquired;
		try
		{
			acquired = folderLock.WaitOne(TimeSpan.Zero);
		}
		catch (AbandonedMutexException)
		{
			// The previous owner ended without letting go, most likely a crash; the folder is free.
			acquired = true;
		}

		if (!acquired)
		{
			folderLock.Dispose();
			return false;
		}

		_folderLock = folderLock;
		return true;
	}

	/// <summary>The UI thread's queue, once WinUI runs. A launch handed over before that is ignored: the window comes up anyway.</summary>
	public static void ListenOn(DispatcherQueue queue) => _uiQueue = queue;

	// Case and a trailing separator do not make another folder on Windows. Hashed so the key has a fixed length and
	// no path characters, whatever the folder is called.
	private static string KeyFor(string dataRoot)
	{
		string folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataRoot)).ToUpperInvariant();
		byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(folder));
		return "Mokaterm-" + Convert.ToHexString(hash, 0, 16);
	}

	// Raised on a thread pool thread.
	private static void OnActivated(object? sender, AppActivationArguments args) => _ = _uiQueue?.TryEnqueue(BringToFront);

	private static void BringToFront()
	{
		IReadOnlyList<Microsoft.Maui.Controls.Window>? windows = MauiApplication.Current?.Windows;
		if (windows is not { Count: > 0 } || windows[0].Handler?.PlatformView is not WinUIWindow window)
		{
			return;
		}

		if (window.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
		{
			presenter.Restore();
		}

		window.Activate();
	}
}
