using Microsoft.UI.Windowing;
using MauiWindow = Microsoft.Maui.Controls.Window;
using WinUIWindow = Microsoft.UI.Xaml.Window;

namespace Mokaterm.Maui.Platforms.Windows;

/// <summary>Brings one of the app's windows to the front, restoring it first when it was minimized.</summary>
internal static class WindowFront
{
	public static void Bring(MauiWindow window)
	{
		ArgumentNullException.ThrowIfNull(window);
		if (window.Handler?.PlatformView is not WinUIWindow native)
		{
			return;
		}

		// Activating a minimized window only flashes it in the taskbar.
		if (native.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
		{
			presenter.Restore();
		}

		native.Activate();
	}
}
