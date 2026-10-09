using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Mokaterm.Maui.Platforms.Windows;

/// <summary>
/// Puts one drop target on every window this app owns under its top level window, and takes them all off again.
///
/// A top level registration on its own is not enough, which is the whole reason this type exists. WinUI draws the page
/// in an island of its own, a child window of class Microsoft.UI.Content.DesktopChildSiteBridge, and the shell looks up
/// the window under the pointer and stops at the first one that carries a drop target. The island carries one that
/// refuses every external drop (microsoft-ui-xaml#10119 and #10142), so the shell never walks up to the window above
/// it. Ours has to be the target it finds, which means it goes on the island as well, and on anything else WinUI or
/// WebView2 puts in between.
/// </summary>
internal sealed class NativeDropRegistration
{
	private const int DragDropAlreadyRegistered = unchecked((int)0x80040101);

	/// <summary>What RegisterDragDrop leaves on a window, which is also how to see that something got there first.</summary>
	private const string DropTargetProperty = "OleDropTargetInterface";

	/// <summary>A window class name is at most 256 characters.</summary>
	private const int MaxClassLength = 256;

	private readonly List<nint> _registered = [];
	private readonly ILogger _logger;

	public NativeDropRegistration(ILogger logger) => _logger = logger;

	/// <summary>
	/// Registers <paramref name="target"/> on <paramref name="window"/> and on every descendant of it that belongs to
	/// this process. Calling it again is free for a window already taken over, which is what lets it run at each point
	/// the window may have grown a child: WinUI builds the island after the window exists, and the one that matters is
	/// the one the pointer ends up over.
	/// </summary>
	public void Register(nint window, IDropTarget target)
	{
		if (window == 0)
		{
			return;
		}

		try
		{
			Sweep(window, target);
		}
		catch (Exception ex)
		{
			// This runs while MAUI is creating the window and again from a WebView event, so nothing may escape it: an
			// app with no drop target has no dropped files, which is what it had before, and the rest of it still works.
			_logger.LogError(ex, "Registering a drop target failed");
		}
	}

	public void RevokeAll()
	{
		// A target left on a window that is gone is a reference the shell still holds, and the next drag over this
		// app's taskbar button reaches a dead object through it.
		foreach (nint window in _registered)
		{
			_ = RevokeDragDrop(window);
		}

		_registered.Clear();
	}

	private void Sweep(nint window, IDropTarget target)
	{
		// A window WinUI rebuilt took its target with it, so its handle is only something to revoke later that cannot
		// be revoked. Without this the list would grow for as long as the app runs, since the sweep runs on activation.
		_ = _registered.RemoveAll(static hwnd => !IsWindow(hwnd));

		List<string> taken = [];
		foreach (nint hwnd in Tree(window))
		{
			if (_registered.Contains(hwnd))
			{
				continue;
			}

			// RegisterDragDrop only works on a window of the calling process, and WebView2 parents windows of the
			// browser process under ours.
			_ = GetWindowThreadProcessId(hwnd, out uint processId);
			if (processId != (uint)Environment.ProcessId)
			{
				continue;
			}

			// Whatever is already there is what does not deliver external drops, so ours takes its place.
			bool replaced = GetPropW(hwnd, DropTargetProperty) != 0;
			if (replaced)
			{
				_ = RevokeDragDrop(hwnd);
			}

			int result = RegisterDragDrop(hwnd, target);
			if (result == DragDropAlreadyRegistered)
			{
				_ = RevokeDragDrop(hwnd);
				result = RegisterDragDrop(hwnd, target);
				replaced = true;
			}

			if (result < 0)
			{
				_logger.LogWarning("Window class {Class} keeps its own drop target (0x{Result:x8}).", ClassOf(hwnd), result);
				continue;
			}

			_registered.Add(hwnd);
			taken.Add(replaced ? ClassOf(hwnd) + " (taken over)" : ClassOf(hwnd));
		}

		if (taken.Count > 0)
		{
			// The one line that says whether a dropped file can arrive at all, and so the first thing to read when it
			// cannot: the island's own window has to be in this list.
			_logger.LogInformation("Dropped files now arrive through {Windows}.", string.Join(", ", taken));
		}
	}

	/// <summary>The window and every descendant of it, parents before children.</summary>
	private static List<nint> Tree(nint window)
	{
		List<nint> windows = [window];

		// EnumChildWindows reaches grandchildren too, which is where WinUI and WebView2 put theirs.
		_ = EnumChildWindows(window, (child, _) =>
		{
			windows.Add(child);
			return true;
		}, 0);

		return windows;
	}

	private static string ClassOf(nint window)
	{
		char[] buffer = new char[MaxClassLength];
		int length = GetClassNameW(window, buffer, buffer.Length);
		return length > 0 ? new string(buffer, 0, length) : "(unnamed)";
	}

	[return: MarshalAs(UnmanagedType.Bool)]
	private delegate bool EnumChildProc(nint window, nint parameter);

	[DllImport("ole32.dll", ExactSpelling = true)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	private static extern int RegisterDragDrop(nint window, IDropTarget target);

	[DllImport("ole32.dll", ExactSpelling = true)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	private static extern int RevokeDragDrop(nint window);

	[DllImport("user32.dll", ExactSpelling = true)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool EnumChildWindows(nint parent, EnumChildProc callback, nint parameter);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	private static extern int GetClassNameW(nint window, [Out] char[] buffer, int length);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	private static extern nint GetPropW(nint window, string name);

	[DllImport("user32.dll", ExactSpelling = true)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

	[DllImport("user32.dll", ExactSpelling = true)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool IsWindow(nint window);
}
