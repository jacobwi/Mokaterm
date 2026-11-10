using System.Runtime.InteropServices;
using Microsoft.Maui.Platform;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Mokaterm.Maui.Services;
using WinUIWindow = Microsoft.UI.Xaml.Window;

namespace Mokaterm.Maui.Platforms.Windows;

// A WinUI window is black until its content draws a first frame, which on a light theme shows as a dark flash in the
// middle of the window's opening. So a new window is cloaked (real, focusable, drawn by nobody) until XAML has drawn,
// and uncloaked then. A timer uncloaks it whatever happens, so a frame that never comes cannot leave it invisible.
internal static class WindowReveal
{
	private const int DwmCloak = 13;
	private const int FramesBeforeReveal = 2;

	private static readonly TimeSpan Longest = TimeSpan.FromMilliseconds(1500);

	// Called from OnWindowCreated, before the window is first shown.
	public static void HoldUntilDrawn(WinUIWindow window)
	{
		nint hwnd = window.GetWindowHandle();
		if (!SetCloak(hwnd, true))
		{
			return;
		}

		int frames = 0;
		bool revealed = false;
		DispatcherQueueTimer timer = window.DispatcherQueue.CreateTimer();

		void Reveal(string step)
		{
			if (revealed)
			{
				return;
			}

			revealed = true;
			CompositionTarget.Rendering -= OnRendering;
			timer.Stop();
			_ = SetCloak(hwnd, false);

			// Windows does not hand the foreground to a cloaked window, so the activation it skipped is asked for again.
			window.Activate();
			StartupTrace.Mark(step);
		}

		// Rendering comes before each frame, so the second one means the first has been drawn.
		void OnRendering(object? sender, object e)
		{
			if (++frames >= FramesBeforeReveal)
			{
				Reveal("Window shown after its first frame");
			}
		}

		CompositionTarget.Rendering += OnRendering;
		timer.Interval = Longest;
		timer.IsRepeating = false;
		timer.Tick += (_, _) => Reveal("Window shown by the timer, with no frame drawn");
		timer.Start();
	}

	private static bool SetCloak(nint hwnd, bool cloaked)
	{
		int value = cloaked ? 1 : 0;
		return DwmSetWindowAttribute(hwnd, DwmCloak, ref value, sizeof(int)) == 0;
	}

	[DllImport("dwmapi.dll", ExactSpelling = true)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
}
