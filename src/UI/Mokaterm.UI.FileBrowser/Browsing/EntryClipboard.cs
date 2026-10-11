using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.UI.FileBrowser.Browsing;

/// <summary>
/// Entries copied or cut in one browser, waiting to be pasted on the same server. It holds paths, not files: pasting
/// copies or moves them there, and the system clipboard is left alone.
/// </summary>
internal sealed record EntryClipboard(IReadOnlyList<RemoteFileEntry> Entries, bool IsCut);
