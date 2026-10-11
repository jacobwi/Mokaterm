using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Platform;

namespace Mokaterm.UI.FileBrowser.Compare;

/// <summary>One relative path of a folder compare, with the copy on each side that has one.</summary>
/// <param name="RelativePath">Below the compared folders, with '/' separators: <c>css/app.css</c>.</param>
internal sealed record FolderCompareRow(string RelativePath, FolderCompareStatus Status, LocalFileItem? Local, RemoteFileEntry? Remote)
{
	/// <summary>True when there is a local copy to send.</summary>
	public bool CanUpload => Local?.OpenReadAsync is not null;
}
