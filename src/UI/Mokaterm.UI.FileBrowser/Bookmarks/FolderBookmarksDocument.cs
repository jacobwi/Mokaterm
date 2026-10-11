namespace Mokaterm.UI.FileBrowser.Bookmarks;

/// <summary>The vault document behind <see cref="FolderBookmarkStore"/>: each login's bookmarked folders, in the order they were added.</summary>
internal sealed class FolderBookmarksDocument
{
	/// <summary>Encrypted with the vault key: a folder path says what a server holds and how it is laid out.</summary>
	public const string DocumentName = "file-bookmarks";

	public Dictionary<Guid, List<string>> Logins { get; set; } = [];
}
