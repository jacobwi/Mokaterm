namespace Mokaterm.UI.Common.Files;

/// <summary>
/// Queues a download of one file from a file system a module brings, through the transfer queue, exactly as the file
/// browser's own Download does: the save dialog when the transfer runs, resuming where the host can keep a partial
/// file, and the checksum check where the server offers one. For a file a module just wrote, such as a backup.
/// </summary>
/// <remarks>Scoped to the page, like <see cref="IFileBrowserDialog"/>. Call it from the renderer's thread.</remarks>
public interface IRemoteFileDownloads
{
	/// <summary>
	/// Looks <paramref name="path"/> up in <paramref name="files"/> and queues it; the transfer names it with
	/// <see cref="FileBrowserRequest.Location"/> and groups it under <see cref="FileBrowserRequest.Title"/>.
	/// </summary>
	/// <exception cref="Abstractions.FileSystem.RemoteFileSystemException">The file is not there, or is not a file.</exception>
	Task DownloadAsync(FileBrowserRequest files, string path, CancellationToken cancellationToken = default);
}
