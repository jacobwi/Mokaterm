namespace Mokaterm.Abstractions.FileSystem;

/// <summary>
/// Archive names: which kind a name is, the folder it unpacks into and the name a new one starts with. The name is all
/// there is to go on, the same way tar is told its decompressor with -z, -j or -J rather than finding out.
/// </summary>
public static class RemoteArchives
{
	/// <summary>What <see cref="IRemoteFileSystem.CompressAsync"/> writes.</summary>
	public const string CompressedExtension = ".tar.gz";

	// Longest first, so ".tar.gz" wins over ".gz" and ".tar" is only what is left.
	private static readonly (string Extension, RemoteArchiveKind Kind)[] Extensions =
	[
		(".tar.bz2", RemoteArchiveKind.TarBzip2),
		(".tar.gz", RemoteArchiveKind.TarGzip),
		(".tar.xz", RemoteArchiveKind.TarXz),
		(".tbz2", RemoteArchiveKind.TarBzip2),
		(".tgz", RemoteArchiveKind.TarGzip),
		(".txz", RemoteArchiveKind.TarXz),
		(".tar", RemoteArchiveKind.Tar),
		(".zip", RemoteArchiveKind.Zip),
	];

	/// <summary>The kind <paramref name="name"/> ends in, or <see cref="RemoteArchiveKind.None"/>. A bare ".zip" is a hidden file, not an archive.</summary>
	public static RemoteArchiveKind KindOf(string name) => Match(name)?.Kind ?? RemoteArchiveKind.None;

	/// <summary>The archive extension <paramref name="name"/> ends in, as it is spelled there, or null.</summary>
	public static string? ExtensionOf(string name) => Match(name) is { } match ? name[^match.Extension.Length..] : null;

	/// <summary>
	/// The folder an archive unpacks into: <c>site</c> for <c>site.tar.gz</c>. Unpacking into a folder of its own means
	/// an archive whose entries sit at its top level cannot spill over or replace what is already in the folder.
	/// </summary>
	public static string FolderName(string archiveName)
	{
		ArgumentNullException.ThrowIfNull(archiveName);
		return Match(archiveName) is { } match ? archiveName[..^match.Extension.Length] : archiveName + " contents";
	}

	/// <summary>
	/// The name a new archive of <paramref name="entryNames"/> starts with: the entry's own name for one, the name of
	/// <paramref name="directory"/> for several, and <c>archive</c> at the root.
	/// </summary>
	public static string ArchiveName(IReadOnlyList<string> entryNames, string directory)
	{
		ArgumentNullException.ThrowIfNull(entryNames);
		string stem = entryNames.Count == 1 ? entryNames[0]
			: RemotePath.IsRoot(directory) ? "archive"
			: RemotePath.GetName(directory);
		return stem + CompressedExtension;
	}

	private static (string Extension, RemoteArchiveKind Kind)? Match(string name)
	{
		ArgumentNullException.ThrowIfNull(name);
		foreach ((string extension, RemoteArchiveKind kind) in Extensions)
		{
			if (name.Length > extension.Length && name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
			{
				return (extension, kind);
			}
		}

		return null;
	}
}
