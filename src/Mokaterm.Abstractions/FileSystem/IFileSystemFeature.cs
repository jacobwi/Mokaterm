namespace Mokaterm.Abstractions.FileSystem;

/// <summary>
/// Session feature for protocols with remote files. For SSH the file system opens its own SFTP connection
/// with the session's credentials, which is how an SSH tab gets its side file browser.
/// </summary>
/// <remarks>
/// Every file system returned here belongs to the session and stays usable until the session closes, which disposes
/// them. Callers never dispose them: transfers queued from a browser that has since unmounted may still be running.
/// </remarks>
public interface IFileSystemFeature
{
	/// <summary>True when <see cref="OpenElevatedAsync"/> is supported.</summary>
	bool SupportsElevation { get; }

	/// <summary>Opens the file system as the logged-in user, or returns the one already open.</summary>
	ValueTask<IRemoteFileSystem> OpenAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Opens a view of the same file system that runs every operation as root. May prompt for a sudo password.
	/// </summary>
	/// <exception cref="NotSupportedException"><see cref="SupportsElevation"/> is false.</exception>
	/// <exception cref="RemoteFileSystemException">Elevation was refused (<see cref="RemoteFileErrorKind.ElevationFailed"/>).</exception>
	ValueTask<IRemoteFileSystem> OpenElevatedAsync(CancellationToken cancellationToken = default);
}
