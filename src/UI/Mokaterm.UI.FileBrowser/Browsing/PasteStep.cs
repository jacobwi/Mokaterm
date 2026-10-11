using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.UI.FileBrowser.Browsing;

/// <summary>One entry of a paste: what is copied, the path the copy gets, and whether it replaces what is there.</summary>
internal sealed record PasteStep(RemoteFileEntry Source, string Destination, bool Overwrite);
