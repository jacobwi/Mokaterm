namespace Mokaterm.UI.FileBrowser.Details;

/// <summary>How the details pane would read an entry for its preview, decided from the listing alone.</summary>
internal enum EntryPreviewKind
{
	/// <summary>Nothing is read: a folder, a special file, a link to anything but a file.</summary>
	None,

	/// <summary>The start of the file, kept only if it is UTF-8 text.</summary>
	Text,

	/// <summary>The whole file, up to the image cap.</summary>
	Image,
}
