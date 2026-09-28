namespace Mokaterm.Maui.Platforms.Windows;

internal static class WindowHandles
{
	/// <summary>The native handle of the main window, needed to parent WinRT pickers in an unpackaged app.</summary>
	public static nint Current()
	{
		IReadOnlyList<Microsoft.Maui.Controls.Window>? windows = Microsoft.Maui.Controls.Application.Current?.Windows;
		Microsoft.Maui.Controls.Window? window = windows is { Count: > 0 } ? windows[0] : null;
		return window?.Handler?.PlatformView is Microsoft.UI.Xaml.Window native
			? WinRT.Interop.WindowNative.GetWindowHandle(native)
			: throw new InvalidOperationException("The main window is not ready.");
	}
}
