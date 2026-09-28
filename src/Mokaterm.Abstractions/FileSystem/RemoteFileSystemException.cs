namespace Mokaterm.Abstractions.FileSystem;

public enum RemoteFileErrorKind
{
	Unknown,
	NotFound,
	PermissionDenied,
	AlreadyExists,
	DirectoryNotEmpty,
	NotSupported,
	ConnectionLost,

	/// <summary>sudo refused: wrong password, user not in sudoers, or a TTY is required.</summary>
	ElevationFailed,
}

/// <summary>A remote file operation failed. <see cref="Kind"/> lets the UI offer "retry as root" on permission errors.</summary>
public sealed class RemoteFileSystemException : IOException
{
	public RemoteFileSystemException()
	{
	}

	public RemoteFileSystemException(string message)
		: base(message)
	{
	}

	public RemoteFileSystemException(string message, Exception innerException)
		: base(message, innerException)
	{
	}

	public RemoteFileSystemException(RemoteFileErrorKind kind, string message, string? path = null, Exception? innerException = null)
		: base(message, innerException)
	{
		Kind = kind;
		Path = path;
	}

	public RemoteFileErrorKind Kind { get; }

	public string? Path { get; }
}
