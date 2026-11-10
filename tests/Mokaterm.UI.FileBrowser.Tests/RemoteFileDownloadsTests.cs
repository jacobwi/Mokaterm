using Microsoft.Extensions.Logging.Abstractions;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Transfers;
using Mokaterm.UI.Common.Files;
using Mokaterm.UI.FileBrowser.Tests.Fakes;
using Mokaterm.UI.FileBrowser.Transfers;

namespace Mokaterm.UI.FileBrowser.Tests;

// A module's download of one file it wrote goes through the browser's own download: one queued transfer, named and
// grouped the way the browser's are.
public sealed class RemoteFileDownloadsTests
{
	private readonly FakeFileSystem _fileSystem = new();
	private readonly FakeQueue _queue = new();
	private readonly RemoteFileDownloads _downloads;

	public RemoteFileDownloadsTests() => _downloads = new RemoteFileDownloads(new RemoteTransferService(
		_queue,
		new NoLocalFiles(),
		new ThrowingInteraction(),
		new FakeSettingsService(),
		TimeProvider.System,
		NullLogger<RemoteTransferService>.Instance));

	[Fact]
	public async Task AFile_IsQueuedAsADownload_NamedForItsHost()
	{
		_fileSystem.Contains("/tmp", FakeFileSystem.File("/tmp/db.tar.gz", size: 2048));

		await _downloads.DownloadAsync(Request(), "/tmp/db.tar.gz", TestContext.Current.CancellationToken);

		TransferRequest queued = Assert.Single(_queue.Requests);
		Assert.Equal("db.tar.gz", queued.Name);
		Assert.Equal(TransferDirection.Download, queued.Direction);
		Assert.Equal("abc@docker-host:/tmp/db.tar.gz", queued.Source);
		Assert.Equal("Docker on docker-host", queued.Group);
		Assert.Equal(2048, queued.TotalBytes);
	}

	[Fact]
	public async Task AFileThatIsGone_OrAFolder_IsRefused_AndNothingIsQueued()
	{
		_fileSystem.Contains("/tmp", FakeFileSystem.Folder("/tmp/backups"));

		RemoteFileSystemException gone = await Assert.ThrowsAsync<RemoteFileSystemException>(() => _downloads.DownloadAsync(Request(), "/tmp/db.tar.gz", TestContext.Current.CancellationToken));
		await Assert.ThrowsAsync<RemoteFileSystemException>(() => _downloads.DownloadAsync(Request(), "/tmp/backups", TestContext.Current.CancellationToken));

		Assert.Equal(RemoteFileErrorKind.NotFound, gone.Kind);
		Assert.Empty(_queue.Requests);
	}

	private FileBrowserRequest Request() => new()
	{
		FileSystem = new FakeFileFeature(_fileSystem),
		Title = "Docker on docker-host",
		Location = "docker-host",
	};
}
