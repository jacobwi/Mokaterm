using System.Runtime.InteropServices;

namespace Mokaterm.Maui.Platforms.Windows;

/// <summary>A plain Win32 message box, for the few reasons not to start that turn up before WinUI has a window.</summary>
internal static class NativeMessageBox
{
	private const uint IconError = 0x10;
	private const uint SetForeground = 0x10000;

	public static void ShowError(string message) => _ = MessageBoxW(0, message, "Mokaterm", IconError | SetForeground);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	private static extern int MessageBoxW(nint owner, string text, string caption, uint type);
}
