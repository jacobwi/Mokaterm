using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Storage;
using Mokaterm.Abstractions.Transfers;
using Mokaterm.UI.Common.Platform;

namespace Mokaterm.Maui.ToolWindows;

/// <summary>
/// The desktop's session tool windows. It only manages windows: the shell decides when one opens and when one has to
/// go. Created before the app is built and given the app's registrations once it is, because a tool window's services
/// are worked out from them (<see cref="ToolPageServices"/>).
/// </summary>
public sealed class DesktopToolWindows : IToolWindows
{
	// A tool window that got its own copy of any of these would be a second vault or a session manager with no
	// sessions in it, so tool windows stay off rather than open like that.
	private static readonly Type[] MustBorrow =
	[
		typeof(IVault),
		typeof(IVaultDataStore),
		typeof(ISessionManager),
		typeof(ITransferQueue),
		typeof(ICredentialStore),
		typeof(IConnectionRepository),
	];

	private readonly Lock _gate = new();
	private readonly List<ToolWindow> _windows = [];
	private ToolPageServices? _pages;
	private ILogger _logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

	public event Action? Changed;

	/// <summary>
	/// Works out from the app's registrations what a tool window borrows. Until this runs, and for good when the
	/// registrations would not let a window borrow the vault and the sessions, every open answers false and the shell
	/// shows the tool in a dialog.
	/// </summary>
	public void Prepare(IEnumerable<ServiceDescriptor> registrations, IEnumerable<Type> pageServices, ILogger<DesktopToolWindows> logger)
	{
		_logger = logger;
		ToolPageServices pages = new(registrations, pageServices);
		if (pages.FirstNotShared(MustBorrow) is { } wrong)
		{
			_logger.LogError(
				"Session tools stay in dialogs: a tool window would get its own {Service} instead of borrowing the main window's.",
				wrong.Name);
			return;
		}

		_pages = pages;
	}

	public bool IsOpen(Guid sessionId, string toolKey)
	{
		lock (_gate)
		{
			return _windows.Exists(window => window.Shows(sessionId, toolKey));
		}
	}

	public Task<bool> OpenAsync(ToolWindowRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		return MainThread.InvokeOnMainThreadAsync(() => Open(request));
	}

	public void Close(Guid sessionId, string? toolKey = null) => CloseWhere(window => window.Shows(sessionId, toolKey));

	public void CloseAll(IServiceProvider services) => CloseWhere(window => ReferenceEquals(window.Request.Services, services));

	/// <summary>Every tool window, for the main window closing: the process must not outlive it in a tool window.</summary>
	public void CloseEverything() => CloseWhere(_ => true);

	public void ShowOwner(IServiceProvider services) => OnMainThread(() =>
	{
		bool hasWindows;
		lock (_gate)
		{
			hasWindows = _windows.Exists(window => ReferenceEquals(window.Request.Services, services));
		}

		// One window owns every scope that opens tool windows, so the owner is the window that is not one of them.
		if (hasWindows && Application.Current?.Windows.FirstOrDefault(window => window is not ToolWindow) is { } owner)
		{
			Bring(owner);
		}
	});

	private static void Bring(Window window)
	{
#if WINDOWS
		Platforms.Windows.WindowFront.Bring(window);
#else
		Application.Current?.ActivateWindow(window);
#endif
	}

	private static bool IsMinimized(Window window)
	{
#if WINDOWS
		return Platforms.Windows.ToolWindowPlacement.IsMinimized(window);
#else
		return false;
#endif
	}

	private static ToolWindowBounds? Placement(ToolWindowBounds? bounds)
	{
		if (bounds is not { IsUsable: true })
		{
			return null;
		}

#if WINDOWS
		// A place on a screen that is gone is dropped, the size kept: the system then puts the window somewhere it shows.
		double scale = Application.Current?.Windows.FirstOrDefault(window => window is not ToolWindow)?.DisplayDensity ?? 1;
		return Platforms.Windows.ToolWindowPlacement.IsOnScreen(bounds, scale) ? bounds : bounds with { X = double.NaN, Y = double.NaN };
#else
		return bounds;
#endif
	}

	private bool Open(ToolWindowRequest request)
	{
		if (_pages is not { } pages || Application.Current is not { } app)
		{
			return false;
		}

		ToolWindow? existing;
		lock (_gate)
		{
			existing = _windows.Find(window => window.Shows(request.SessionId, request.ToolKey));
		}

		if (existing is not null)
		{
			Bring(existing);
			return true;
		}

		ToolWindow created;
		try
		{
			created = new ToolWindow(request, pages.Borrow(request.Services), Placement(request.Bounds));
		}
		catch (Exception ex)
		{
			_logger.LogWarning("A tool window could not be set up: {Failure}", LogSafe.Describe(ex));
			return false;
		}

		created.Destroying += OnWindowDestroying;
		lock (_gate)
		{
			_windows.Add(created);
		}

		try
		{
			app.OpenWindow(created);
		}
		catch
		{
			Abandon(app, created);
			throw;
		}

		Changed?.Invoke();
		return true;
	}

	// A window MAUI failed to build must not stay listed: the next open would bring it forward instead of trying again,
	// and the shell would count the tool as open in a window with nothing in it.
	private void Abandon(Application app, ToolWindow window)
	{
		window.Destroying -= OnWindowDestroying;
		lock (_gate)
		{
			_windows.Remove(window);
		}

		try
		{
			app.CloseWindow(window);
		}
		catch (Exception ex)
		{
			_logger.LogWarning("A tool window that failed to open could not be closed: {Failure}", LogSafe.Describe(ex));
		}
	}

	// At once on the UI thread: the main window's close handler calls this and the process must not wait for a queue.
	private static void OnMainThread(Action action)
	{
		if (MainThread.IsMainThread)
		{
			action();
		}
		else
		{
			MainThread.BeginInvokeOnMainThread(action);
		}
	}

	private void CloseWhere(Predicate<ToolWindow> match) => OnMainThread(() =>
	{
		ToolWindow[] closing;
		lock (_gate)
		{
			closing = [.. _windows.FindAll(match)];
		}

		foreach (ToolWindow window in closing)
		{
			Application.Current?.CloseWindow(window);
		}
	});

	private void OnWindowDestroying(object? sender, EventArgs e)
	{
		if (sender is not ToolWindow window)
		{
			return;
		}

		window.Destroying -= OnWindowDestroying;
		bool removed;
		lock (_gate)
		{
			removed = _windows.Remove(window);
		}

		if (window.CurrentBounds is { IsUsable: true } bounds && !IsMinimized(window))
		{
			window.Request.BoundsChanged?.Invoke(bounds);
		}

		if (removed)
		{
			Changed?.Invoke();
		}
	}
}
