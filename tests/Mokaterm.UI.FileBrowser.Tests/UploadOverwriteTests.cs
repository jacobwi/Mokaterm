using Microsoft.Extensions.Logging.Abstractions;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Transfers;
using Mokaterm.UI.FileBrowser.Tests.Fakes;
using Mokaterm.UI.FileBrowser.Transfers;
using static Mokaterm.UI.FileBrowser.Tests.Fakes.FakeFileSystem;

namespace Mokaterm.UI.FileBrowser.Tests;

/// <summary>The overwrite rule a caller passes for one batch, which the folder compare uses because it already asked.</summary>
public sealed class UploadOverwriteTests
{
	private static readonly TransferOrigin Origin = new("abc@build-01", "build-01");

	private readonly FakeFileSystem _fileSystem = new FakeFileSystem()
		.Contains("/srv/site", File("/srv/site/index.html", size: 5), Folder("/srv/site/css"))
		.Contains("/srv/site/css", File("/srv/site/css/app.css", size: 7));

	private readonly FakeQueue _queue = new();

	[Fact]
	public async Task GivenOverwrite_ReplacesWithoutAskingOrLooking()
	{
		await Service().UploadAsync(
			new TransferFileSystem(_fileSystem, session: null, connection: null),
			Origin,
			"/srv/site",
			[Local("index.html"), Local("css/app.css")],
			OverwriteBehavior.Overwrite,
			TestContext.Current.CancellationToken);
		foreach (TransferRequest request in _queue.Requests)
		{
			await request.ExecuteAsync(new TransferContext(new Progress<long>(), _ => { }, CancellationToken.None));
		}

		Assert.Equal(["mkdir /srv/site/css", "put -f /srv/site/index.html", "put -f /srv/site/css/app.css"], _fileSystem.Calls);
	}

	// The settings say ask, and the prompt throws here: without the batch's own rule the same upload would have asked.
	[Fact]
	public async Task WithoutOne_TheSettingsDecide()
	{
		await Assert.ThrowsAsync<InvalidOperationException>(() => Service().UploadAsync(
			new TransferFileSystem(_fileSystem, session: null, connection: null),
			Origin,
			"/srv/site",
			[Local("index.html")],
			TestContext.Current.CancellationToken));
	}

	private RemoteTransferService Service() => new(
		_queue,
		new NoLocalFiles(),
		new ThrowingInteraction(),
		new FakeSettingsService(),
		TimeProvider.System,
		NullLogger<RemoteTransferService>.Instance);

	private static LocalFileItem Local(string relativePath) => new()
	{
		Name = relativePath[(relativePath.LastIndexOf('/') + 1)..],
		RelativePath = relativePath,
		Length = 3,
		OpenReadAsync = _ => ValueTask.FromResult<Stream>(new MemoryStream([1, 2, 3])),
	};
}
