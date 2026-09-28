namespace Mokaterm.Abstractions.FileSystem;

public enum RemoteEntryKind
{
	File,
	Directory,
	SymbolicLink,
	Other,
}

[Flags]
public enum RemoteFileSystemFeatures
{
	None = 0,
	Permissions = 1 << 0,
	Ownership = 1 << 1,
	SymbolicLinks = 1 << 2,
	Elevation = 1 << 3,

	/// <summary><see cref="IRemoteFileSystem.ListAccountsAsync"/> can read the server's users and groups.</summary>
	Accounts = 1 << 4,
}

/// <summary>One directory entry.</summary>
public sealed record RemoteFileEntry
{
	public required string Name { get; init; }

	/// <summary>Absolute path of the entry itself (not of a link's target).</summary>
	public required string Path { get; init; }

	public required RemoteEntryKind Kind { get; init; }

	public long Size { get; init; }

	public DateTimeOffset? LastModified { get; init; }

	public UnixFileMode? Permissions { get; init; }

	public string? Owner { get; init; }

	public string? Group { get; init; }

	public string? LinkTarget { get; init; }

	/// <summary>What a symbolic link points at, when the server could resolve it.</summary>
	public RemoteEntryKind? LinkTargetKind { get; init; }

	public bool IsHidden => Name.StartsWith('.');

	/// <summary>True for directories and for links that point at directories: both can be opened.</summary>
	public bool IsDirectoryLike => Kind == RemoteEntryKind.Directory || LinkTargetKind == RemoteEntryKind.Directory;
}

public sealed record UploadOptions
{
	public static UploadOptions Default { get; } = new();

	public bool Overwrite { get; init; }

	/// <summary>Applied after the upload. Null leaves the server default (umask).</summary>
	public UnixFileMode? Permissions { get; init; }

	/// <summary>Applied after the upload when the protocol supports setting times.</summary>
	public DateTimeOffset? LastModified { get; init; }
}
