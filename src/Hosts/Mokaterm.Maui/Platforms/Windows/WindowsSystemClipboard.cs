using System.Runtime.InteropServices;
using Mokaterm.UI.Common.Platform;

namespace Mokaterm.Maui.Platforms.Windows;

/// <summary>
/// The Windows clipboard through user32, for clearing a copied secret that no page can clear: the window is closing, or
/// the page waits to be focused again before it may write the clipboard. It reads nothing but the clipboard's sequence
/// number, which Windows moves on every change by any program, and empties the clipboard only while that number is the
/// one recorded after our copy.
/// </summary>
internal sealed class WindowsSystemClipboard : ISystemClipboard
{
	// Another program usually holds the clipboard for a few milliseconds while it writes; trying often costs nothing.
	private static readonly TimeSpan RetryInterval = TimeSpan.FromMilliseconds(15);

	// Past the caller's timeout, so a worker that is about to finish its last try is not given up on.
	private static readonly TimeSpan JoinGrace = TimeSpan.FromMilliseconds(250);

	public uint? ContentNumber
	{
		get
		{
			// Zero means this process has no clipboard access in its window station, not a number to compare against.
			uint number = GetClipboardSequenceNumber();
			return number == 0 ? null : number;
		}
	}

	public SystemClipboardClear EmptyIfUnchanged(uint contentNumber, TimeSpan timeout)
	{
		try
		{
			// OpenClipboard ties the clipboard to the calling thread until CloseClipboard, and EmptyClipboard sends the last
			// writer a message and waits for it, which a hung WebView would never answer. A thread of our own keeps both away
			// from the caller, which at shutdown is the window's own thread, and is simply left behind if it does not finish.
			SystemClipboardClear result = SystemClipboardClear.Busy;
			Thread worker = new(() => result = Empty(contentNumber, timeout))
			{
				IsBackground = true,
				Name = "Clipboard clear",
			};
			worker.SetApartmentState(ApartmentState.STA);
			worker.Start();
			return worker.Join(timeout + JoinGrace) ? result : SystemClipboardClear.Busy;
		}
		catch (Exception)
		{
			// No thread to run on; the contract is never to throw, so the secret simply stays.
			return SystemClipboardClear.Failed;
		}
	}

	private static SystemClipboardClear Empty(uint contentNumber, TimeSpan timeout)
	{
		long deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
		while (true)
		{
			if (GetClipboardSequenceNumber() != contentNumber)
			{
				return SystemClipboardClear.Changed;
			}

			// Null owner: nothing is put on the clipboard afterwards, so no window of ours has to stay around for it.
			if (OpenClipboard(0))
			{
				try
				{
					// Checked again with the clipboard held, so no program can write between this check and the clear.
					if (GetClipboardSequenceNumber() != contentNumber)
					{
						return SystemClipboardClear.Changed;
					}

					return EmptyClipboard() ? SystemClipboardClear.Cleared : SystemClipboardClear.Failed;
				}
				finally
				{
					_ = CloseClipboard();
				}
			}

			if (Environment.TickCount64 >= deadline)
			{
				return SystemClipboardClear.Busy;
			}

			Thread.Sleep(RetryInterval);
		}
	}

	[DllImport("user32.dll", ExactSpelling = true)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	private static extern uint GetClipboardSequenceNumber();

	[DllImport("user32.dll", ExactSpelling = true)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool OpenClipboard(nint newOwner);

	[DllImport("user32.dll", ExactSpelling = true)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool EmptyClipboard();

	[DllImport("user32.dll", ExactSpelling = true)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool CloseClipboard();
}
