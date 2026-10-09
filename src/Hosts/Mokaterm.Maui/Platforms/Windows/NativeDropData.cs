using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Mokaterm.Maui.Platforms.Windows;

/// <summary>
/// Reads dropped file paths out of an OLE data object. Only <c>CF_HDROP</c> on global memory is asked for: Explorer,
/// the desktop and every file manager offer it, and it is the only format that names files on disk rather than handing
/// over their contents.
/// </summary>
internal static class NativeDropData
{
	private const short ClipboardFormatHDrop = 15;

	/// <summary>The index that asks <c>DragQueryFile</c> how many files there are instead of for one of them.</summary>
	private const uint FileCount = 0xFFFFFFFF;

	/// <summary>A path is at most 32767 characters once it carries the \\?\ prefix, plus its terminator.</summary>
	private const int MaxPathLength = 32768;

	/// <summary>
	/// Whether the drag names files at all. Answered from the source's format list, so it costs nothing to ask while
	/// the mouse moves, and it is what decides whether the drop can be accepted: a dragged selection of text must show
	/// the "no drop" cursor instead of looking droppable.
	/// </summary>
	public static bool CarriesFiles(IDataObject? data)
	{
		if (data is null)
		{
			return false;
		}

		FORMATETC format = FileFormat();
		return data.QueryGetData(ref format) == 0;
	}

	/// <summary>The dropped paths, folders included, in the order the source listed them.</summary>
	public static IReadOnlyList<string> ReadPaths(IDataObject? data)
	{
		if (data is null)
		{
			return [];
		}

		FORMATETC format = FileFormat();
		data.GetData(ref format, out STGMEDIUM medium);
		try
		{
			// For CF_HDROP the block of global memory is the HDROP, which is what DragQueryFile takes.
			return medium.tymed == TYMED.TYMED_HGLOBAL && medium.unionmember != 0 ? Read(medium.unionmember) : [];
		}
		finally
		{
			// The medium belongs to us from here on; skipping this leaves the drop allocated for as long as the app runs.
			ReleaseStgMedium(ref medium);
		}
	}

	private static FORMATETC FileFormat() => new()
	{
		cfFormat = ClipboardFormatHDrop,
		ptd = 0,
		dwAspect = DVASPECT.DVASPECT_CONTENT,
		lindex = -1,
		tymed = TYMED.TYMED_HGLOBAL,
	};

	private static List<string> Read(nint drop)
	{
		uint count = DragQueryFileW(drop, FileCount, null, 0);
		if (count == 0)
		{
			return [];
		}

		char[] buffer = new char[MaxPathLength];

		// The count is the source's word, so the list starts small rather than sized for whatever it claimed.
		List<string> paths = new((int)Math.Min(count, 1024u));
		for (uint index = 0; index < count; index++)
		{
			// The length never counts the terminator, so a path that filled the buffer was cut and is left out.
			uint length = DragQueryFileW(drop, index, buffer, (uint)buffer.Length);
			if (length > 0 && length < buffer.Length)
			{
				paths.Add(new string(buffer, 0, (int)length));
			}
		}

		return paths;
	}

	[DllImport("ole32.dll", ExactSpelling = true)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	private static extern void ReleaseStgMedium(ref STGMEDIUM medium);

	[DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	private static extern uint DragQueryFileW(nint drop, uint file, [Out] char[]? buffer, uint length);
}
