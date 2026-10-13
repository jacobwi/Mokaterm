using Microsoft.UI.Windowing;
using Mokaterm.UI.Common.Platform;
using Windows.Graphics;
using MauiWindow = Microsoft.Maui.Controls.Window;
using WinUIWindow = Microsoft.UI.Xaml.Window;

namespace Mokaterm.Maui.Platforms.Windows;

/// <summary>
/// Where a tool window is and how it stacks: whether its remembered place is still on a screen, which a screen unplugged
/// since may not be, whether it is minimized, and keeping it on top.
/// </summary>
internal static class ToolWindowPlacement
{
	// The title bar is what the window is dragged by, so its middle has to be reachable.
	private const double TitleBarMiddle = 16;

	/// <param name="bounds">The remembered place, in device-independent units.</param>
	/// <param name="scale">Pixels per unit on the main window's screen, the nearest guess at the target screen's.</param>
	public static bool IsOnScreen(ToolWindowBounds bounds, double scale)
	{
		ArgumentNullException.ThrowIfNull(bounds);
		if (!double.IsFinite(scale) || scale <= 0)
		{
			scale = 1;
		}

		PointInt32 point = new(
			(int)Math.Round((bounds.X + (bounds.Width / 2)) * scale),
			(int)Math.Round((bounds.Y + TitleBarMiddle) * scale));
		return DisplayArea.GetFromPoint(point, DisplayAreaFallback.None) is not null;
	}

	/// <summary>
	/// A minimized window reports a place far off every screen, which is not where the user left it, so it is not
	/// remembered.
	/// </summary>
	public static bool IsMinimized(MauiWindow window)
	{
		ArgumentNullException.ThrowIfNull(window);
		return window.Handler?.PlatformView is WinUIWindow native
			&& native.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized };
	}

	/// <summary>
	/// Keeps the window above every window that is not itself kept on top, the main window included, or lets it go
	/// behind them again. Does nothing before the native window exists.
	/// </summary>
	public static void SetKeepOnTop(MauiWindow window, bool keepOnTop)
	{
		ArgumentNullException.ThrowIfNull(window);
		if (window.Handler?.PlatformView is WinUIWindow native && native.AppWindow.Presenter is OverlappedPresenter presenter)
		{
			presenter.IsAlwaysOnTop = keepOnTop;
		}
	}
}
