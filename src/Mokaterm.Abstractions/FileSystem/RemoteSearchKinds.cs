namespace Mokaterm.Abstractions.FileSystem;

/// <summary>The kinds of entries a <see cref="RemoteSearchQuery"/> returns.</summary>
[Flags]
public enum RemoteSearchKinds
{
	None = 0,

	/// <summary>Everything that cannot be opened as a folder: files, links to files, broken links, devices.</summary>
	Files = 1 << 0,

	/// <summary>Folders and links to folders, as <see cref="RemoteFileEntry.IsDirectoryLike"/> says.</summary>
	Folders = 1 << 1,

	All = Files | Folders,
}
