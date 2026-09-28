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
	/// Returns the entry, or null when nothing exists at <paramref name="path"/>. Like <c>lstat</c>, a symbolic link is
	/// described as the link itself, with <see cref="RemoteFileEntry.LinkTargetKind"/> telling what it points at.
	/// </summary>
	ValueTask<RemoteFileEntry?> StatAsync(string path, CancellationToken cancellationToken = default);

	ValueTask CreateDirectoryAsync(string path, CancellationToken cancellationToken = default);

	/// <summary>Deletes a file, link or directory. Non-empty directories need <paramref name="recursive"/>.</summary>
	ValueTask DeleteAsync(string path, bool recursive, CancellationToken cancellationToken = default);

	/// <summary>Moves or renames. Fails with <see cref="RemoteFileErrorKind.AlreadyExists"/> unless <paramref name="overwrite"/>.</summary>
	ValueTask RenameAsync(string sourcePath, string destinationPath, bool overwrite, CancellationToken cancellationToken = default);

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
}
