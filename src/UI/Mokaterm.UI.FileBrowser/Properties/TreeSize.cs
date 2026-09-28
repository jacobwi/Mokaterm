namespace Mokaterm.UI.FileBrowser.Properties;

/// <summary>What a walk through a selection has counted so far.</summary>
/// <param name="Files">Files, devices, sockets and pipes.</param>
/// <param name="Folders">Folders inside the selection, not the selected folders themselves.</param>
/// <param name="Links">Links, which are counted but never followed.</param>
/// <param name="Bytes">The size of the regular files.</param>
/// <param name="UnreadableFolders">Folders that could not be listed, so their contents are missing from the totals.</param>
/// <param name="Truncated">The walk stopped at its limit before it saw everything.</param>
public readonly record struct TreeSize(long Files, long Folders, long Links, long Bytes, long UnreadableFolders, bool Truncated)
{
	public long Items => Files + Folders + Links;
}
