using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Platform;
using Microsoft.Web.WebView2.Core;
using Mokaterm.Maui.Services;
using MauiWindow = Microsoft.Maui.Controls.Window;
using WebView2 = Microsoft.UI.Xaml.Controls.WebView2;
using WinPoint = Windows.Foundation.Point;
using WinUIWindow = Microsoft.UI.Xaml.Window;

namespace Mokaterm.Maui.Platforms.Windows;

/// <summary>
/// The window's own drop target, which is how files dragged out of Explorer reach the page at all. WinUI refuses an
/// external drop before a single drag event reaches WebView2 content (microsoft-ui-xaml#10142 and #10119,
/// WebView2Feedback#4908), so the page never sees dragenter, dragover or drop and the identical script that works on
/// the web host does nothing here. A target of our own goes on every window the app owns instead, which is where the
/// shell looks and ahead of anything WinUI does with the drag; <see cref="NativeDropRegistration"/> says why the top
/// level window alone is not one of those places.
///
/// Everything a browser would do for us then has to be done here: the point comes in as screen pixels and goes to the
/// page in CSS pixels (<see cref="NativeDropPoint"/>), the page hit tests it against its drop zones and moves the
/// overlay, and the paths come out of the data object by hand. The page is called through the WebView's own script
/// channel rather than Blazor interop, because a COM callback runs on the window's thread inside the shell's drag
/// loop: that is exactly where the WebView wants to be called and nowhere a wait of any kind belongs.
/// </summary>
internal sealed class NativeFileDrop : IDropTarget
{
	// Modifier bits in an OLE drop's key state, and the only effect an upload can be.
	private const uint MkShift = 0x0004;
	private const uint MkControl = 0x0008;
	private const uint MkAlt = 0x0020;
	private const uint DropEffectNone = 0;
	private const uint DropEffectCopy = 1;

	// DragOver arrives for every mouse move the shell sees, which is far more often than an overlay has to move.
	private const long OverIntervalMilliseconds = 80;

	// A drop outside every zone is never claimed, so its paths are dropped again after a while. Longer than the
	// bridge's own wait for them, so an upload that is starting has always taken them first.
	private static readonly TimeSpan UnclaimedLifetime = TimeSpan.FromSeconds(30);

	private readonly DesktopDropCoordinator _coordinator;
	private readonly ILogger<NativeFileDrop> _logger;
	private readonly NativeDropRegistration _targets;

	private WinUIWindow? _window;
	private WebView2? _webView;
	private CoreWebView2? _core;
	private nint _hwnd;

	// Set on the way in and read until the drag ends: the data object cannot change during one drag.
	private bool _carriesFiles;
	private long _lastOverAt;

	// Said once each, because both are about how the app was put together rather than about one drag.
	private bool _sawDrag;
	private bool _saidPageMissing;

	public NativeFileDrop(DesktopDropCoordinator coordinator, ILogger<NativeFileDrop> logger)
	{
		_coordinator = coordinator;
		_logger = logger;
		_targets = new NativeDropRegistration(logger);
	}

	/// <summary>Takes over drops for a window MAUI just created. One window, so one target.</summary>
	public void Attach(WinUIWindow native)
	{
		if (native is null || ReferenceEquals(native, _window))
		{
			return;
		}

		_window = native;
		_hwnd = native.GetWindowHandle();
		native.Closed += OnWindowClosed;

		// WinUI builds the island this app is drawn in after the window exists, and that island is the window the
		// pointer is over, so the sweep has to run again later. Activation is the cheapest moment that is always
		// after it, and it also covers an island WinUI rebuilt (microsoft-ui-xaml#10231).
		native.Activated += OnWindowActivated;

		if (native.GetWindow() is MauiWindow window
			&& window.Page?.GetVisualTreeDescendants().OfType<BlazorWebView>().FirstOrDefault() is { } view)
		{
			view.BlazorWebViewInitialized += (_, e) => AttachWebView(e.WebView);
		}
		else
		{
			// The drop would still arrive, but with no way to tell the page where the pointer is there is no overlay
			// and nothing to hand the paths to.
			_logger.LogWarning("The window has no BlazorWebView, so a dropped file cannot reach the page.");
		}

		if (PrepareOle())
		{
			_targets.Register(_hwnd, this);
		}
	}

	public void DragEnter(IDataObject? data, uint keyState, long point, ref uint effect)
	{
		try
		{
			_carriesFiles = NativeDropData.CarriesFiles(data);
			effect = Effect(effect);
			if (_carriesFiles)
			{
				if (!_sawDrag)
				{
					// Said once per run, and it is the answer to "the overlay never shows": with this line the shell
					// reaches us and the rest is the page's half, without it the target is on the wrong window.
					_sawDrag = true;
					_logger.LogInformation("The window's drop target is receiving dragged files.");
				}

				ReportOver(point, force: true);
			}
		}
		catch (Exception ex)
		{
			// Inside the shell's drag loop there is nobody to tell and nothing to retry: a failure here costs the
			// overlay, and refusing the drag is the honest answer.
			_carriesFiles = false;
			effect = DropEffectNone;
			_logger.LogDebug(ex, "Starting a native file drag failed");
		}
	}

	public void DragOver(uint keyState, long point, ref uint effect)
	{
		try
		{
			effect = Effect(effect);
			if (_carriesFiles)
			{
				ReportOver(point, force: false);
			}
		}
		catch (Exception ex)
		{
			_carriesFiles = false;
			effect = DropEffectNone;
			_logger.LogDebug(ex, "Following a native file drag failed");
		}
	}

	public void DragLeave()
	{
		_carriesFiles = false;
		CallPage("window.mokatermNativeDrop?.leave()");
	}

	public void Drop(IDataObject? data, uint keyState, long point, ref uint effect)
	{
		_carriesFiles = false;
		try
		{
			IReadOnlyList<string> paths = NativeDropData.ReadPaths(data);
			if (paths.Count == 0)
			{
				effect = DropEffectNone;
				CallPage("window.mokatermNativeDrop?.leave()");
				return;
			}

			// The id carries its paths before the page hears about the drop, so the bridge's wait for them is already
			// over when it starts. Hexadecimal, which is also why it needs no quoting rules in the script below.
			string dropId = Guid.NewGuid().ToString("N");
			_coordinator.Complete(dropId, paths);
			_ = ForgetUnclaimedAsync(dropId);

			(double x, double y) = ToPage(point);
			CallPage(string.Create(
				CultureInfo.InvariantCulture,
				$"window.mokatermNativeDrop?.drop({x:0.###},{y:0.###},'{dropId}',{Held(keyState, MkControl)},{Held(keyState, MkShift)},{Held(keyState, MkAlt)})"));
			effect = DropEffectCopy;
		}
		catch (Exception ex)
		{
			effect = DropEffectNone;
			CallPage("window.mokatermNativeDrop?.leave()");
			_logger.LogError(ex, "Reading a native file drop failed");
		}
	}

	// Lower case, because True is not a JavaScript literal.
	private static string Held(uint keyState, uint modifier) => (keyState & modifier) != 0 ? "true" : "false";

	/// <summary>
	/// True for what a call into a closing WebView comes back as, and for a script that threw. The wide tier, the way
	/// <c>JsModule.IsExpected</c> reads it on the Blazor side: a drag is a gesture with nowhere to report anything.
	/// </summary>
	private static bool IsExpected(Exception exception) =>
		exception is COMException or ObjectDisposedException or InvalidOperationException;

	private void AttachWebView(WebView2 webView)
	{
		_webView = webView;
		_core = webView.CoreWebView2;

		// By now WinUI has the page in its island, which is the window a drag is actually over.
		_targets.Register(_hwnd, this);
	}

	/// <summary>
	/// RegisterDragDrop needs OLE on the calling thread, which is the window's own. WinUI has initialized COM here
	/// already, so this normally answers S_FALSE, and that counts as success. Nothing uninitializes it again: the
	/// count is per thread, this thread ends with the process, and WinUI needs OLE all that time.
	/// </summary>
	private bool PrepareOle()
	{
		try
		{
			int initialized = OleInitialize(0);
			if (initialized >= 0)
			{
				return true;
			}

			// RPC_E_CHANGED_MODE: the thread belongs to the multi threaded apartment, where OLE drag and drop has no
			// message loop to run on.
			_logger.LogWarning("OLE is not available on the window's thread (0x{Result:x8}), so dropped files cannot arrive.", initialized);
		}
		catch (Exception ex)
		{
			// This runs while MAUI is creating the window, so nothing may escape it.
			_logger.LogError(ex, "Preparing OLE for dropped files failed");
		}

		return false;
	}

	private void OnWindowActivated(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs args) =>
		_targets.Register(_hwnd, this);

	private void OnWindowClosed(object sender, Microsoft.UI.Xaml.WindowEventArgs args)
	{
		_targets.RevokeAll();

		if (_window is not null)
		{
			_window.Closed -= OnWindowClosed;
			_window.Activated -= OnWindowActivated;
		}

		_window = null;
		_webView = null;
		_core = null;
		_hwnd = 0;
	}

	private void ReportOver(long point, bool force)
	{
		long now = Environment.TickCount64;
		if (!force && now - _lastOverAt < OverIntervalMilliseconds)
		{
			return;
		}

		_lastOverAt = now;
		(double x, double y) = ToPage(point);
		CallPage(string.Create(CultureInfo.InvariantCulture, $"window.mokatermNativeDrop?.over({x:0.###},{y:0.###})"));
	}

	// Copy is all an upload can be, and only for a drag that names files: anything else shows the "no drop" cursor
	// rather than looking droppable. The effect the shell hands in is what the source allows, so it is kept in.
	private uint Effect(uint allowed) =>
		_carriesFiles && (allowed & DropEffectCopy) != 0 ? DropEffectCopy : DropEffectNone;

	private (double X, double Y) ToPage(long point)
	{
		(int screenX, int screenY) = NativeDropPoint.ScreenPoint(point);
		PointL client = new(screenX, screenY);
		if (!ScreenToClient(_hwnd, ref client))
		{
			// The window has gone; off the page is where the point belongs then, and the overlay goes out with it.
			return (-1, -1);
		}

		WinPoint origin = WebViewLayout.Origin(_webView);
		return NativeDropPoint.ToCss(client.X, client.Y, GetDpiForWindow(_hwnd), origin.X, origin.Y);
	}

	private void CallPage(string script)
	{
		CoreWebView2? core = _core;
		if (core is null)
		{
			if (!_saidPageMissing)
			{
				// The other half of a drag that arrives and shows nothing: the shell reached us, the page did not.
				_saidPageMissing = true;
				_logger.LogWarning("A file was dragged over the window before the page was ready.");
			}

			return;
		}

		// Fire and forget on purpose: this runs inside the shell's drag loop, which no answer may hold up.
		_ = RunScriptAsync(core, script);
	}

	private async Task RunScriptAsync(CoreWebView2 core, string script)
	{
		try
		{
			await core.ExecuteScriptAsync(script);
		}
		catch (Exception ex) when (IsExpected(ex))
		{
			// The WebView is closing with the window, and the overlay it was going to move is going with it.
		}
		catch (Exception ex)
		{
			// Nobody is waiting on this and a drag must not end over it, so an unexpected failure is reported and
			// nothing more. Without this line it would be a task nothing ever looks at.
			_logger.LogDebug(ex, "Telling the page about a native file drag failed");
		}
	}

	private async Task ForgetUnclaimedAsync(string dropId)
	{
		await Task.Delay(UnclaimedLifetime);
		_coordinator.Forget(dropId);
	}

	[DllImport("ole32.dll", ExactSpelling = true)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	private static extern int OleInitialize(nint reserved);

	[DllImport("user32.dll", ExactSpelling = true)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool ScreenToClient(nint window, ref PointL point);

	[DllImport("user32.dll", ExactSpelling = true)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	private static extern uint GetDpiForWindow(nint window);
}
