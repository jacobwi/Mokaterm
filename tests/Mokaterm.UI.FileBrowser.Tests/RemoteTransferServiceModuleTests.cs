using Mokaterm.Abstractions.FileSystem;
using Mokaterm.UI.FileBrowser.Tests.Fakes;
using Mokaterm.UI.FileBrowser.Transfers;

namespace Mokaterm.UI.FileBrowser.Tests;

// Transfers queued from a file system a module handed over, such as a container's files.
public sealed partial class RemoteTransferServiceTests
{
	[Fact]
	public async Task Retry_OfAModulesFiles_GoesThroughWhatItsFeatureHandsOutNow()
	{
		FakeFileFeature feature = new(_fileSystem) { SupportsElevation = false };
		_fileSystem.UploadFailures.Enqueue(new RemoteFileSystemException(RemoteFileErrorKind.ConnectionLost, "web-1 is not running.", "/srv/upload/report.pdf"));
		await _service.UploadAsync(new TransferFileSystem(_fileSystem, feature), Origin, Target, [LocalFile("report.pdf", "report.pdf")], TestContext.Current.CancellationToken);

		await Assert.ThrowsAsync<RemoteFileSystemException>(() => _queue.RunAsync(0));
		FakeFileSystem reopened = new();
		feature.User = reopened;
		await _queue.RunAsync(0);

		Assert.Equal(["put /srv/upload/report.pdf"], _fileSystem.Calls);
		Assert.Equal(["put -f /srv/upload/report.pdf"], reopened.Calls);
		Assert.Equal(0, feature.ElevatedOpens);
	}
}
