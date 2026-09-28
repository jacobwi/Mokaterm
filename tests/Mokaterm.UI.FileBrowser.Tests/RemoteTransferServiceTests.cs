using Microsoft.Extensions.Logging.Abstractions;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Abstractions.Transfers;
using Mokaterm.UI.FileBrowser.Tests.Fakes;
using Mokaterm.UI.FileBrowser.Transfers;

namespace Mokaterm.UI.FileBrowser.Tests;

public sealed class RemoteTransferServiceTests
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
		FileFeature feature = new(_fileSystem);
		Tab session = new(feature);
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
		FileFeature feature = new(_fileSystem);
		Tab session = new(feature);
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
		Tab session = new(new FileFeature(_fileSystem));
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
		Item[] batch = [new("a"), new("b"), new("c")];
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
		Item[] batch = [new("a"), new("b")];
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

	/// <summary>Keeps what was enqueued and runs it on request, as the queue would when a transfer starts or is retried.</summary>
	private sealed class FakeQueue : ITransferQueue
	{
		private Action? _changed;

		public List<TransferRequest> Requests { get; } = [];

		public int ItemsReads { get; private set; }

		public int Subscribers => _changed?.GetInvocationList().Length ?? 0;

		public IReadOnlyList<ITransferItem> Items
		{
			get
			{
				ItemsReads++;
				return [];
			}
		}

		public int ActiveCount => 0;

		public event Action? Changed
		{
			add => _changed += value;
			remove => _changed -= value;
		}

		public ITransferItem Enqueue(TransferRequest request)
		{
			Requests.Add(request);
			return new Item(request.Name);
		}

		public Task RunAsync(int index) =>
			Requests[index].ExecuteAsync(new TransferContext(new Progress<long>(), _ => { }, CancellationToken.None));

		public void RaiseChanged() => _changed?.Invoke();

		public void Cancel(Guid id) => throw new NotSupportedException();

		public void Retry(Guid id) => throw new NotSupportedException();

		public void Remove(Guid id) => throw new NotSupportedException();

		public void ClearFinished() => throw new NotSupportedException();
	}

	/// <summary>A live item, the way the queue hands them out: its state changes in place.</summary>
	private sealed class Item(string name) : ITransferItem
	{
		public Guid Id { get; } = Guid.NewGuid();

		public string Name => name;

		public TransferDirection Direction => TransferDirection.Upload;

		public string Source => "";

		public string Destination => "";

		public string? Group => null;

		public bool Elevated => false;

		public TransferState State { get; set; } = TransferState.Queued;

		public long? TotalBytes => null;

		public long TransferredBytes => 0;

		public double BytesPerSecond => 0;

		public string? Error => null;

		public DateTimeOffset QueuedAt => DateTimeOffset.UnixEpoch;

		public DateTimeOffset? StartedAt => null;

		public DateTimeOffset? FinishedAt => null;
	}

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

	private sealed class NoLocalFiles : ILocalFileAccess
	{
		public bool CanPickFolders => false;

		public ValueTask<IReadOnlyList<LocalFileItem>> PickFilesAsync(bool multiple = true, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public ValueTask<IReadOnlyList<LocalFileItem>> PickFolderAsync(CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public ValueTask<bool> SaveFileAsync(string suggestedName, long? length, Func<Stream, CancellationToken, Task> writeAsync, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();
	}

	/// <summary>A session tab whose connection can drop and come back, each connection a new object.</summary>
	private sealed class Tab(FileFeature feature) : ISessionHandle
	{
		public event Action? Changed
		{
			add { }
			remove { }
		}

		public Guid Id { get; } = Guid.NewGuid();

		public string Title => "abc@build-01";

		public HostProfile Host { get; } = new() { Id = Guid.NewGuid(), Address = "build-01" };

		public ConnectionProfile Connection { get; } = new() { Id = Guid.NewGuid(), HostId = Guid.NewGuid(), ProtocolId = "ssh" };

		public ProtocolDescriptor Protocol { get; } = new() { Id = "ssh", DisplayName = "SSH", DefaultPort = 22, Capabilities = ProtocolCapabilities.FileSystem };

		public bool IsTransient => false;

		public SessionState State => Session is null ? SessionState.Disconnected : SessionState.Connected;

		public string? StatusMessage => null;

		public ConnectFailure? Failure => null;

		public DateTimeOffset OpenedAt => DateTimeOffset.UnixEpoch;

		public DateTimeOffset? ConnectedAt => DateTimeOffset.UnixEpoch;

		public ITerminalStream? Terminal => null;

		public IProtocolSession? Session { get; private set; } = new LiveConnection(feature);

		public void Reconnect() => Session = new LiveConnection(feature);

		public void Disconnect() => Session = null;
	}

	private sealed class LiveConnection(FileFeature feature) : IProtocolSession
	{
		public Task Completion => Task.CompletedTask;

		public TFeature? GetFeature<TFeature>()
			where TFeature : class => feature as TFeature;

		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}

	/// <summary>Hands out whichever file systems the test put in place, and counts the root views it opened.</summary>
	private sealed class FileFeature(IRemoteFileSystem user) : IFileSystemFeature
	{
		public IRemoteFileSystem User { get; set; } = user;

		public IRemoteFileSystem? Root { get; set; }

		public int ElevatedOpens { get; private set; }

		public bool SupportsElevation => true;

		public ValueTask<IRemoteFileSystem> OpenAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(User);

		public ValueTask<IRemoteFileSystem> OpenElevatedAsync(CancellationToken cancellationToken = default)
		{
			ElevatedOpens++;
			return ValueTask.FromResult(Root ?? throw new RemoteFileSystemException(RemoteFileErrorKind.ElevationFailed, "No root view."));
		}
	}
}
