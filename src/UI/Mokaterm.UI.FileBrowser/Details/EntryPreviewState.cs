namespace Mokaterm.UI.FileBrowser.Details;

/// <summary>What came of trying to preview a file.</summary>
internal enum EntryPreviewState
{
	/// <summary>Not a file the pane previews: a folder, a special file, a link to anything but a file.</summary>
	None,

	/// <summary>The start of a text file, in <see cref="EntryPreview.Text"/>.</summary>
	Text,

	/// <summary>An image, as a data address in <see cref="EntryPreview.ImageAddress"/>.</summary>
	Image,

	/// <summary>The file holds nothing.</summary>
	Empty,

	/// <summary>An image past <see cref="EntryPreviews.ImageBytes"/>.</summary>
	TooLarge,

	/// <summary>Holds a NUL byte, so it is not text.</summary>
	Binary,

	/// <summary>Not valid UTF-8, which the pane would have to show with replacement characters.</summary>
	NotUtf8,

	/// <summary>The read failed, as <see cref="EntryPreview.Problem"/> says.</summary>
	Failed,
}
