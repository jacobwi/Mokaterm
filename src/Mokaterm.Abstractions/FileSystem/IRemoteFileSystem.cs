namespace Mokaterm.Abstractions.FileSystem;

/// <summary>
/// POSIX-style remote file access. Paths are absolute and use '/'; see <see cref="RemotePath"/>. Failures
/// surface as <see cref="RemoteFileSystemException"/> so the UI can react the same way for every protocol.
/// Implementations serialize access internally, so callers may issue operations concurrently.
/// </summary>
public interface IRemoteFileSystem : IAsyncDisposable
{
	RemoteFileSystemFeatures Features { get; }

	/// <summary>The account operations run as, for example <c>abc</c> or <c>root</c>.</summary>
	string UserName { get; }

	/// <summary>True for the root view returned by <see cref="IFileSystemFeature.OpenElevatedAsync"/>.</summary>
	bool IsElevated { get; }

	/// <summary>Where the browser starts: the user's home directory, or <c>/</c> when the server has none.</summary>
	ValueTask<string> GetHomeDirectoryAsync(CancellationToken cancellationToken = default);

	/// <summary>Resolves <c>~</c>, <c>.</c>, <c>..</c> and symbolic links to an absolute path.</summary>
	ValueTask<string> ResolvePathAsync(string path, CancellationToken cancellationToken = default);

	/// <summary>Lists a directory without the <c>.</c> and <c>..</c> entries.</summary>
	ValueTask<IReadOnlyList<RemoteFileEntry>> ListAsync(string path, CancellationToken cancellationToken = default);

	/// <summary>
	/// Finds what <paramref name="query"/> matches in <paramref name="root"/> and below, streaming each entry as it is found
	/// and stopping at the query's caps. <paramref name="progress"/> hears about every folder read and, last, how the walk
	/// ended. The default is <see cref="RemoteTreeWalker.SearchAsync"/>: one listing at a time, breadth first, never
	/// through a link, with folders that cannot be read counted and skipped.
	/// </summary>
	IAsyncEnumerable<RemoteFileEntry> SearchAsync(
		string root,
		RemoteSearchQuery query,
		IProgress<RemoteSearchProgress>? progress = null,
		CancellationToken cancellationToken = default) =>
		RemoteFileSystemDefaults.SearchAsync(this, root, query, progress, cancellationToken);

	/// <summary>
	/// Returns the entry, or null when nothing exists at <paramref name="path"/>. Like <c>lstat</c>, a symbolic link is
	/// described as the link itself, with <see cref="RemoteFileEntry.LinkTargetKind"/> telling what it points at.
	/// </summary>
	ValueTask<RemoteFileEntry?> StatAsync(string path, CancellationToken cancellationToken = default);

	ValueTask CreateDirectoryAsync(string path, CancellationToken cancellationToken = default);

	/// <summary>Deletes a file, link or directory. Non-empty directories need <paramref name="recursive"/>.</summary>
	ValueTask DeleteAsync(string path, bool recursive, CancellationToken cancellationToken = default);

	/// <summary>Moves or renames. Fails with <see cref="RemoteFileErrorKind.AlreadyExists"/> unless <paramref name="overwrite"/>.</summary>
	ValueTask RenameAsync(string sourcePath, string destinationPath, bool overwrite, CancellationToken cancellationToken = default);

	/// <summary>
	/// Copies a file, link or folder to <paramref name="destinationPath"/> on the server, a folder with everything in it,
	/// keeping modes, times and links as links. Fails with <see cref="RemoteFileErrorKind.AlreadyExists"/> unless
	/// <paramref name="overwrite"/>; overwriting a folder with a folder merges into it. Requires
	/// <see cref="RemoteFileSystemFeatures.Copy"/>, and the default fails with <see cref="RemoteFileErrorKind.NotSupported"/>.
	/// </summary>
	ValueTask CopyAsync(string sourcePath, string destinationPath, bool overwrite, CancellationToken cancellationToken = default) =>
		RemoteFileSystemDefaults.NotSupported("copy on the server", sourcePath);

	/// <summary>
	/// Packs <paramref name="names"/>, entries of <paramref name="directory"/>, into a new gzip-compressed tar at
	/// <paramref name="archivePath"/>. Never replaces anything: an existing path fails with
	/// <see cref="RemoteFileErrorKind.AlreadyExists"/>. Requires <see cref="RemoteFileSystemFeatures.Archives"/>.
	/// </summary>
	ValueTask CompressAsync(string directory, IReadOnlyList<string> names, string archivePath, CancellationToken cancellationToken = default) =>
		RemoteFileSystemDefaults.NotSupported("make archives on the server", archivePath);

	/// <summary>
	/// Unpacks the archive at <paramref name="archivePath"/>, of a kind <see cref="RemoteArchives.KindOf"/> names, into
	/// <paramref name="destinationDirectory"/>, which it creates and which must not exist yet. Requires
	/// <see cref="RemoteFileSystemFeatures.Archives"/>.
	/// </summary>
	ValueTask ExtractAsync(string archivePath, string destinationDirectory, CancellationToken cancellationToken = default) =>
		RemoteFileSystemDefaults.NotSupported("unpack archives on the server", archivePath);

	/// <summary>Requires <see cref="RemoteFileSystemFeatures.Permissions"/>.</summary>
	ValueTask SetPermissionsAsync(string path, UnixFileMode permissions, bool recursive, CancellationToken cancellationToken = default);

	/// <summary>Requires <see cref="RemoteFileSystemFeatures.Ownership"/>. Owner and group are names, not ids.</summary>
	ValueTask SetOwnerAsync(string path, string owner, string? group, bool recursive, CancellationToken cancellationToken = default);

	/// <summary>
	/// Changes the group and keeps every owner as it is. Requires <see cref="RemoteFileSystemFeatures.Ownership"/>. The
	/// default passes each entry's current owner to <see cref="SetOwnerAsync"/>, one entry at a time, and leaves links
	/// inside folders alone.
	/// </summary>
	ValueTask SetGroupAsync(string path, string group, bool recursive, CancellationToken cancellationToken = default) =>
		RemoteFileSystemDefaults.SetGroupAsync(this, path, group, recursive, cancellationToken);

	/// <summary>
	/// Applies <paramref name="change"/> to <paramref name="path"/> and, when it is recursive, to everything inside. Requires
	/// <see cref="RemoteFileSystemFeatures.Permissions"/>. The default hands a plain mode for everything to
	/// <see cref="SetPermissionsAsync"/> and otherwise changes one entry at a time.
	/// </summary>
	ValueTask ChangePermissionsAsync(string path, PermissionChange change, CancellationToken cancellationToken = default) =>
		RemoteFileSystemDefaults.ChangePermissionsAsync(this, path, change, cancellationToken);

	/// <summary>
	/// The server's users and groups, for choosing an owner and group. Empty without
	/// <see cref="RemoteFileSystemFeatures.Accounts"/>, and when the server does not let them be read.
	/// </summary>
	ValueTask<RemoteAccounts> ListAccountsAsync(CancellationToken cancellationToken = default) =>
		ValueTask.FromResult(RemoteAccounts.Empty);

	/// <summary>Opens a file for reading, for previews and streaming downloads. The caller disposes the stream.</summary>
	ValueTask<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default);

	/// <summary>Writes <paramref name="source"/> to <paramref name="path"/>, reporting cumulative bytes to <paramref name="progress"/>.</summary>
	ValueTask UploadAsync(string path, Stream source, UploadOptions options, IProgress<long>? progress = null, CancellationToken cancellationToken = default);

	/// <summary>Copies the file at <paramref name="path"/> into <paramref name="destination"/>, reporting cumulative bytes.</summary>
	ValueTask DownloadAsync(string path, Stream destination, IProgress<long>? progress = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Copies the file at <paramref name="path"/> from byte <paramref name="offset"/> on, which is how a download that failed
	/// part way continues. Progress counts from the offset. The default reads and drops what comes before it, so it is
	/// right everywhere and only saves time where an implementation can start reading at the offset.
	/// </summary>
	ValueTask DownloadAsync(string path, Stream destination, long offset, IProgress<long>? progress = null, CancellationToken cancellationToken = default) =>
		RemoteDownloadDefaults.DownloadFromAsync(this, path, destination, offset, progress, cancellationToken);

	/// <summary>
	/// The algorithm <see cref="ComputeChecksumAsync"/> answers in, or null when this file system has no way to ask the
	/// server. A transfer that verifies hashes its bytes in this algorithm while they pass, so it has to know before it
	/// starts. It may turn null after a first attempt found the server cannot do it after all.
	/// </summary>
	ChecksumAlgorithm? ServerChecksum => null;

	/// <summary>
	/// The checksum of the file at <paramref name="path"/> as the server computes it, in <see cref="ServerChecksum"/> and as
	/// lower-case hex, or null when the server turned out unable to. Never computed by reading the file back here: that
	/// would be a second transfer of the whole file.
	/// </summary>
	ValueTask<string?> ComputeChecksumAsync(string path, CancellationToken cancellationToken = default) =>
		ValueTask.FromResult<string?>(null);
}
