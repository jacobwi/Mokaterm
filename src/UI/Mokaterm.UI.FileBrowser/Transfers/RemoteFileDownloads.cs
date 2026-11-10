using Mokaterm.Abstractions.FileSystem;
using Mokaterm.UI.Common.Files;

namespace Mokaterm.UI.FileBrowser.Transfers;

internal sealed class RemoteFileDownloads : IRemoteFileDownloads
{
	private readonly RemoteTransferService _transfers;

	public RemoteFileDownloads(RemoteTransferService transfers) => _transfers = transfers;

	public async Task DownloadAsync(FileBrowserRequest files, string path, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(files);
		ArgumentNullException.ThrowIfNull(path);
		IRemoteFileSystem fileSystem = await files.FileSystem.OpenAsync(cancellationToken);
		RemoteFileEntry entry = await fileSystem.StatAsync(path, cancellationToken)
			?? throw new RemoteFileSystemException(RemoteFileErrorKind.NotFound, $"{path} is not there any more.", path);
		if (entry.Kind != RemoteEntryKind.File)
		{
			throw new RemoteFileSystemException(RemoteFileErrorKind.Unknown, $"{path} is not a file.", path);
		}

		_transfers.EnqueueDownload(new TransferFileSystem(fileSystem, files.FileSystem), new TransferOrigin(files.Title, files.Location), entry);
	}
}
