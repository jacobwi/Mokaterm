using Microsoft.Extensions.Logging.Abstractions;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Transfers;
using Mokaterm.UI.FileBrowser.Tests.Fakes;
using Mokaterm.UI.FileBrowser.Transfers;

namespace Mokaterm.UI.FileBrowser.Tests;

public sealed partial class RemoteTransferServiceTests
{
	private const string Target = "/srv/upload";

	private static readonly TransferOrigin Origin = new("abc@build-01", "build-01");

	private readonly FakeFileSystem _fileSystem = new();
	private readonly FakeQueue _queue = new();
	private readonly RemoteTransferService _service;

	public RemoteTransferServiceTests() => _service = new RemoteTransferService(
		_queue,
		new NoLocalFiles(),
		new ThrowingInteraction(),
		new DefaultSettings(),
		TimeProvider.System,
		NullLogger<RemoteTransferService>.Instance);

	[Fact]
	public async Task Upload_DotDotSegments_StayInsideTheTargetFolder()
	{
		await _service.UploadAsync(QueuedWith(_fileSystem), Origin, Target, [LocalFile("evil", "../../etc/cron.d/evil")], TestContext.Current.CancellationToken);
		await _queue.RunAsync(0);

		Assert.Equal(["mkdir /srv/upload/etc", "mkdir /srv/upload/etc/cron.d", "put /srv/upload/etc/cron.d/evil"], _fileSystem.Calls);
	}

	// With no usable segment in the path, the name was used as it was, and ".." then pointed at the target's parent.
	[Theory]
	[InlineData("..", "..")]
	[InlineData("", "..")]
	[InlineData(".", ".")]
	public async Task Upload_WithoutAUsableName_IsSkipped(string relativePath, string name)
	{
		UploadBatch batch = await _service.UploadAsync(QueuedWith(_fileSystem), Origin, Target, [LocalFile(name, relativePath)], TestContext.Current.CancellationToken);

		Assert.Empty(batch.Transfers);
		Assert.Empty(_queue.Requests);
		Assert.Empty(_fileSystem.Calls);
	}

	// The file checked as absent when the upload was queued, then appeared before it ran. It is not a leftover of this
	// upload, so a retry must not replace it.
	[Fact]
	public async Task Retry_AfterTheServerRefusedToReplace_StillDoesNotOverwrite()
	{
		_fileSystem.UploadFailures.Enqueue(new RemoteFileSystemException(RemoteFileErrorKind.AlreadyExists, "report.pdf already exists", "/srv/upload/report.pdf"));
		await _service.UploadAsync(QueuedWith(_fileSystem), Origin, Target, [LocalFile("report.pdf", "report.pdf")], TestContext.Current.CancellationToken);

		await Assert.ThrowsAsync<RemoteFileSystemException>(() => _queue.RunAsync(0));
		await _queue.RunAsync(0);

		Assert.Equal(["put /srv/upload/report.pdf", "put /srv/upload/report.pdf"], _fileSystem.Calls);
	}

	[Fact]
	public async Task Retry_AfterAFailureMidUpload_ReplacesWhatWasLeft()
	{
		_fileSystem.UploadFailures.Enqueue(new RemoteFileSystemException(RemoteFileErrorKind.ConnectionLost, "The connection was lost.", "/srv/upload/report.pdf"));
		await _service.UploadAsync(QueuedWith(_fileSystem), Origin, Target, [LocalFile("report.pdf", "report.pdf")], TestContext.Current.CancellationToken);

		await Assert.ThrowsAsync<RemoteFileSystemException>(() => _queue.RunAsync(0));
		await _queue.RunAsync(0);

		Assert.Equal(["put /srv/upload/report.pdf", "put -f /srv/upload/report.pdf"], _fileSystem.Calls);
	}

	// The session's SFTP connection dropped and reopened between the attempts, which closed the file system the upload was
	// queued with; the retry used to go to that closed one and fail again.
	[Fact]
	public async Task Retry_AfterTheSessionReopenedItsFileSystem_UploadsThroughTheNewOne()
	{
		FakeFileFeature feature = new(_fileSystem);
		FakeTab session = new(feature);
		_fileSystem.UploadFailures.Enqueue(new RemoteFileSystemException(RemoteFileErrorKind.ConnectionLost, "The SFTP connection is closed.", "/srv/upload/report.pdf"));
		await _service.UploadAsync(new TransferFileSystem(_fileSystem, session, session.Session), Origin, Target, [LocalFile("report.pdf", "report.pdf")], TestContext.Current.CancellationToken);

		await Assert.ThrowsAsync<RemoteFileSystemException>(() => _queue.RunAsync(0));
		FakeFileSystem reopened = new();
		feature.User = reopened;
		await _queue.RunAsync(0);

		Assert.Equal(["put /srv/upload/report.pdf"], _fileSystem.Calls);
		Assert.Equal(["put -f /srv/upload/report.pdf"], reopened.Calls);
	}

	[Fact]
	public async Task Retry_AfterTheSessionReconnected_OpensRootOnceOnTheNewConnection()
	{
		FakeFileSystem root = new() { Elevated = true };
		FakeFileFeature feature = new(_fileSystem);
		FakeTab session = new(feature);
		TransferFileSystem target = new(root, session, session.Session);
		root.UploadFailures.Enqueue(new RemoteFileSystemException(RemoteFileErrorKind.ConnectionLost, "The session is closed.", "/srv/upload/a"));
		root.UploadFailures.Enqueue(new RemoteFileSystemException(RemoteFileErrorKind.ConnectionLost, "The session is closed.", "/srv/upload/b"));
		await _service.UploadAsync(target, Origin, Target, [LocalFile("a", "a"), LocalFile("b", "b")], TestContext.Current.CancellationToken);
		await Assert.ThrowsAsync<RemoteFileSystemException>(() => _queue.RunAsync(0));
		await Assert.ThrowsAsync<RemoteFileSystemException>(() => _queue.RunAsync(1));

		FakeFileSystem newRoot = new() { Elevated = true };
		feature.Root = newRoot;
		session.Reconnect();
		await _queue.RunAsync(0);
		await _queue.RunAsync(1);

		Assert.Equal(1, feature.ElevatedOpens);
		Assert.Equal(["put -f /srv/upload/a", "put -f /srv/upload/b"], newRoot.Calls);
		Assert.All(_queue.Requests, request => Assert.True(request.Elevated));
	}

	[Fact]
	public async Task Run_WhileTheSessionIsDisconnected_FailsAsALostConnection()
	{
		FakeTab session = new(new FakeFileFeature(_fileSystem));
		await _service.UploadAsync(new TransferFileSystem(_fileSystem, session, session.Session), Origin, Target, [LocalFile("report.pdf", "report.pdf")], TestContext.Current.CancellationToken);
		session.Disconnect();

		RemoteFileSystemException failure = await Assert.ThrowsAsync<RemoteFileSystemException>(() => _queue.RunAsync(0));

		Assert.Equal(RemoteFileErrorKind.ConnectionLost, failure.Kind);
		Assert.Empty(_fileSystem.Calls);
	}

	// Every start, end and progress step of every transfer raises Changed; reading the whole queue on each one made a batch
	// of thousands of uploads quadratic.
	[Fact]
	public async Task WaitForCompletion_FollowsTheBatchWithoutReadingTheQueue()
	{
		FakeTransferItem[] batch = [new("a"), new("b"), new("c")];
		Task wait = _service.WaitForCompletionAsync(batch, TestContext.Current.CancellationToken);

		batch[0].State = TransferState.Completed;
		batch[1].State = TransferState.Running;
		_queue.RaiseChanged();
		Assert.False(wait.IsCompleted);

		batch[1].State = TransferState.Failed;
		batch[2].State = TransferState.Canceled;
		_queue.RaiseChanged();
		await wait.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

		Assert.Equal(0, _queue.ItemsReads);
		Assert.Equal(0, _queue.Subscribers);
	}

	[Fact]
	public async Task WaitForCompletion_ATransferRetriedWhileOthersRun_IsWaitedForAgain()
	{
		FakeTransferItem[] batch = [new("a"), new("b")];
		Task wait = _service.WaitForCompletionAsync(batch, TestContext.Current.CancellationToken);
		batch[0].State = TransferState.Failed;
		batch[1].State = TransferState.Running;
		_queue.RaiseChanged();

		batch[0].State = TransferState.Queued;
		batch[1].State = TransferState.Completed;
		_queue.RaiseChanged();
		Assert.False(wait.IsCompleted);

		batch[0].State = TransferState.Completed;
		_queue.RaiseChanged();
		await wait.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
	}

	private static TransferFileSystem QueuedWith(IRemoteFileSystem fileSystem) => new(fileSystem, session: null, connection: null);

	private static LocalFileItem LocalFile(string name, string relativePath) => new()
	{
		Name = name,
		RelativePath = relativePath,
		Length = 3,
		OpenReadAsync = _ => ValueTask.FromResult<Stream>(new MemoryStream([1, 2, 3])),
	};

	/// <summary>Every section at its defaults: overwrites are asked about.</summary>
	private sealed class DefaultSettings : ISettingsService
	{
		public event Action<string>? Changed
		{
			add { }
			remove { }
		}

		public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public T Get<T>()
			where T : class, ISettingsSection, new() => new();

		public Task UpdateAsync<T>(Func<T, T> update, CancellationToken cancellationToken = default)
			where T : class, ISettingsSection, new() => throw new NotSupportedException();

		public Task ResetAsync<T>(CancellationToken cancellationToken = default)
			where T : class, ISettingsSection, new() => throw new NotSupportedException();

		public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
	}
}
