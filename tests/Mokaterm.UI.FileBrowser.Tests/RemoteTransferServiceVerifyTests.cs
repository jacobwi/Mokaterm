using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Transfers;
using Mokaterm.UI.FileBrowser.Tests.Fakes;
using Mokaterm.UI.FileBrowser.Transfers;

namespace Mokaterm.UI.FileBrowser.Tests;

/// <summary>Checking finished transfers against the server's checksum, and continuing ones that failed part way.</summary>
public sealed partial class RemoteTransferServiceTests
{
	private const string BigName = "big.bin";
	private const string BigPath = Target + "/" + BigName;
	private const string Download = "/srv/data.bin";

	// Past the size where an upload keeps a partial file for resuming.
	private static readonly byte[] Big = RandomNumberGenerator.GetBytes((int)FileTransferSettings.MinResumableBytes + 500_000);

	[Fact]
	public async Task Upload_ToAServerThatComputesChecksums_IsVerified()
	{
		_fileSystem.ServerChecksum = ChecksumAlgorithm.Sha256;
		await _service.UploadAsync(QueuedWith(_fileSystem), Origin, Target, [BigFile(Big)], TestContext.Current.CancellationToken);
		Attempt attempt = new();

		await RunAsync(0, attempt);

		Assert.Equal([TransferVerification.Checking, TransferVerification.Verified], attempt.Verifications);
		Assert.Contains("sum " + BigPath, _fileSystem.Calls);
	}

	[Fact]
	public async Task Upload_ThatArrivesDamaged_FailsAndTheRetryReplacesTheCopy()
	{
		_fileSystem.ServerChecksum = ChecksumAlgorithm.Sha256;
		_fileSystem.ChecksumAnswers[BigPath] = new string('0', 64);
		await _service.UploadAsync(QueuedWith(_fileSystem), Origin, Target, [BigFile(Big)], TestContext.Current.CancellationToken);

		RemoteFileSystemException failure = await Assert.ThrowsAsync<RemoteFileSystemException>(() => RunAsync(0, new Attempt()));
		_ = _fileSystem.ChecksumAnswers.Remove(BigPath);
		await RunAsync(0, new Attempt());

		Assert.Contains("arrived damaged", failure.Message, StringComparison.Ordinal);
		Assert.Equal(["put " + BigPath, "put -f " + BigPath], _fileSystem.Calls.Where(call => call.StartsWith("put", StringComparison.Ordinal)));
	}

	[Fact]
	public async Task Upload_ToAServerThatTurnsOutUnableToCompute_IsMarkedUnchecked()
	{
		_fileSystem.ServerChecksum = ChecksumAlgorithm.Sha256;
		_fileSystem.ChecksumAnswers[BigPath] = null;
		await _service.UploadAsync(QueuedWith(_fileSystem), Origin, Target, [BigFile(Big)], TestContext.Current.CancellationToken);
		Attempt attempt = new();

		await RunAsync(0, attempt);

		Assert.Equal(TransferVerification.Unavailable, attempt.Verifications.Last());
	}

	[Fact]
	public async Task Upload_WithVerifyingTurnedOff_NeverAsksTheServer()
	{
		_fileSystem.ServerChecksum = ChecksumAlgorithm.Sha256;
		RemoteTransferService service = Service(new FileTransferSettings { VerifyTransfers = false });
		await service.UploadAsync(QueuedWith(_fileSystem), Origin, Target, [BigFile(Big)], TestContext.Current.CancellationToken);
		Attempt attempt = new();

		await RunAsync(0, attempt);

		Assert.Empty(attempt.Verifications);
		Assert.DoesNotContain(_fileSystem.Calls, call => call.StartsWith("sum", StringComparison.Ordinal));
	}

	// The partial file sits beside the target, so the target was never touched and the retry must not start replacing.
	[Fact]
	public async Task Upload_AfterADropMidway_ContinuesFromThePartialFile()
	{
		_fileSystem.UploadDropAfter = 700_000;
		await _service.UploadAsync(QueuedWith(_fileSystem), Origin, Target, [BigFile(Big)], TestContext.Current.CancellationToken);

		await Assert.ThrowsAsync<RemoteFileSystemException>(() => RunAsync(0, new Attempt()));
		Assert.Equal(700_000, _fileSystem.Contents[BigPath + ".part"].Length);
		Attempt retry = new();
		await RunAsync(0, retry);

		Assert.Equal(Big, _fileSystem.Contents[BigPath]);
		Assert.False(_fileSystem.Contents.ContainsKey(BigPath + ".part"));
		Assert.Equal([700_000], retry.Resumed);
		Assert.Equal(Big.Length, retry.Progress.Last());
		Assert.Equal(["put " + BigPath, "put " + BigPath], _fileSystem.Calls.Where(call => call.StartsWith("put", StringComparison.Ordinal)));
	}

	// The part the earlier attempt sent is read again here and hashed, so the check covers the whole file.
	[Fact]
	public async Task Upload_ResumedAndVerified_ChecksTheWholeFile()
	{
		_fileSystem.ServerChecksum = ChecksumAlgorithm.Sha256;
		_fileSystem.UploadDropAfter = 400_000;
		await _service.UploadAsync(QueuedWith(_fileSystem), Origin, Target, [BigFile(Big)], TestContext.Current.CancellationToken);

		await Assert.ThrowsAsync<RemoteFileSystemException>(() => RunAsync(0, new Attempt()));
		Attempt retry = new();
		await RunAsync(0, retry);

		Assert.Equal([400_000], retry.Resumed);
		Assert.Equal(TransferVerification.Verified, retry.Verifications.Last());
	}

	// FTP writes in place, so its partial file is the target, which the retry has to append to.
	[Fact]
	public async Task Upload_ResumingInPlace_ReplacesItsOwnPartialTarget()
	{
		_fileSystem.PartialBesideTarget = false;
		_fileSystem.UploadDropAfter = 300_000;
		await _service.UploadAsync(QueuedWith(_fileSystem), Origin, Target, [BigFile(Big)], TestContext.Current.CancellationToken);

		await Assert.ThrowsAsync<RemoteFileSystemException>(() => RunAsync(0, new Attempt()));
		Attempt retry = new();
		await RunAsync(0, retry);

		Assert.Equal(Big, _fileSystem.Contents[BigPath]);
		Assert.Equal([300_000], retry.Resumed);
		Assert.Equal(["put " + BigPath, "put -f " + BigPath], _fileSystem.Calls.Where(call => call.StartsWith("put", StringComparison.Ordinal)));
	}

	[Fact]
	public async Task Upload_BelowTheResumeSize_StartsOver()
	{
		byte[] small = RandomNumberGenerator.GetBytes(50_000);
		_fileSystem.UploadDropAfter = 20_000;
		await _service.UploadAsync(QueuedWith(_fileSystem), Origin, Target, [BigFile(small)], TestContext.Current.CancellationToken);

		await Assert.ThrowsAsync<RemoteFileSystemException>(() => RunAsync(0, new Attempt()));
		Attempt retry = new();
		await RunAsync(0, retry);

		Assert.Empty(retry.Resumed);
		Assert.Equal(small, _fileSystem.Contents[BigPath]);
	}

	[Fact]
	public async Task Upload_OfALocalFileThatChangedSize_DropsThePartialFileAndStartsOver()
	{
		byte[] content = Big;
		LocalFileItem file = new()
		{
			Name = BigName,
			RelativePath = BigName,
			Length = Big.Length,
			OpenReadAsync = _ => ValueTask.FromResult<Stream>(new MemoryStream(content)),
		};
		_fileSystem.UploadDropAfter = 500_000;
		await _service.UploadAsync(QueuedWith(_fileSystem), Origin, Target, [file], TestContext.Current.CancellationToken);
		await Assert.ThrowsAsync<RemoteFileSystemException>(() => RunAsync(0, new Attempt()));

		content = RandomNumberGenerator.GetBytes(Big.Length + 10);
		Attempt retry = new();
		await RunAsync(0, retry);

		Assert.Contains("rm " + BigPath + ".part", _fileSystem.Calls);
		Assert.Empty(retry.Resumed);
		Assert.Equal(content, _fileSystem.Contents[BigPath]);
	}

	[Fact]
	public async Task Discard_RemovesThePartialFileBesideTheTarget()
	{
		_fileSystem.UploadDropAfter = 500_000;
		await _service.UploadAsync(QueuedWith(_fileSystem), Origin, Target, [BigFile(Big)], TestContext.Current.CancellationToken);
		await Assert.ThrowsAsync<RemoteFileSystemException>(() => RunAsync(0, new Attempt()));

		await _queue.Requests[0].DiscardAsync!();

		Assert.Contains("rm " + BigPath + ".part", _fileSystem.Calls);
		Assert.False(_fileSystem.Contents.ContainsKey(BigPath + ".part"));
	}

	// It may be all that is left of a file the upload was replacing.
	[Fact]
	public async Task Discard_LeavesAPartialTargetAlone()
	{
		_fileSystem.PartialBesideTarget = false;
		_fileSystem.UploadDropAfter = 500_000;
		await _service.UploadAsync(QueuedWith(_fileSystem), Origin, Target, [BigFile(Big)], TestContext.Current.CancellationToken);
		await Assert.ThrowsAsync<RemoteFileSystemException>(() => RunAsync(0, new Attempt()));

		await _queue.Requests[0].DiscardAsync!();

		Assert.DoesNotContain(_fileSystem.Calls, call => call.StartsWith("rm", StringComparison.Ordinal));
	}

	[Fact]
	public async Task Download_FromAServerThatComputesChecksums_IsVerified()
	{
		MemoryLocalFiles local = new();
		RemoteFileEntry entry = ServeDownload(Big);
		_fileSystem.ServerChecksum = ChecksumAlgorithm.Sha256;
		Service(new FileTransferSettings(), local).EnqueueDownload(QueuedWith(_fileSystem), Origin, entry);
		Attempt attempt = new();

		await RunAsync(0, attempt);

		Assert.Equal(Big, local.Saved);
		Assert.Equal(TransferVerification.Verified, attempt.Verifications.Last());
	}

	[Fact]
	public async Task Download_AfterADropMidway_ContinuesAndChecksTheWholeFile()
	{
		MemoryLocalFiles local = new();
		RemoteFileEntry entry = ServeDownload(Big);
		_fileSystem.ServerChecksum = ChecksumAlgorithm.Sha256;
		_fileSystem.DownloadDropAfter = 600_000;
		Service(new FileTransferSettings(), local).EnqueueDownload(QueuedWith(_fileSystem), Origin, entry);

		await Assert.ThrowsAsync<RemoteFileSystemException>(() => RunAsync(0, new Attempt()));
		Attempt retry = new();
		await RunAsync(0, retry);

		Assert.Equal(Big, local.Saved);
		Assert.Equal([600_000], retry.Resumed);
		Assert.Equal(Big.Length, retry.Progress.Last());
		Assert.Contains($"get {Download} from 600000", _fileSystem.Calls);
		Assert.Equal(TransferVerification.Verified, retry.Verifications.Last());
	}

	[Fact]
	public async Task Download_OfAFileThatChangedOnTheServer_StartsOver()
	{
		MemoryLocalFiles local = new();
		RemoteFileEntry entry = ServeDownload(Big);
		_fileSystem.DownloadDropAfter = 600_000;
		Service(new FileTransferSettings(), local).EnqueueDownload(QueuedWith(_fileSystem), Origin, entry);
		await Assert.ThrowsAsync<RemoteFileSystemException>(() => RunAsync(0, new Attempt()));

		byte[] changed = RandomNumberGenerator.GetBytes(Big.Length - 1000);
		_ = ServeDownload(changed);
		Attempt retry = new();
		await RunAsync(0, retry);

		Assert.Equal(changed, local.Saved);
		Assert.Empty(retry.Resumed);
	}

	[Fact]
	public async Task Download_ThatArrivesDamaged_FailsAndLeavesNothingToContinue()
	{
		MemoryLocalFiles local = new();
		RemoteFileEntry entry = ServeDownload(Big);
		_fileSystem.ServerChecksum = ChecksumAlgorithm.Sha256;
		_fileSystem.ChecksumAnswers[Download] = new string('f', 64);
		Service(new FileTransferSettings(), local).EnqueueDownload(QueuedWith(_fileSystem), Origin, entry);

		RemoteFileSystemException failure = await Assert.ThrowsAsync<RemoteFileSystemException>(() => RunAsync(0, new Attempt()));

		Assert.Contains("arrived damaged", failure.Message, StringComparison.Ordinal);
		Assert.Equal(0, local.PartialLength);
	}

	[Fact]
	public async Task Discard_OfADownload_RemovesItsPartialFile()
	{
		MemoryLocalFiles local = new();
		RemoteFileEntry entry = ServeDownload(Big);
		_fileSystem.DownloadDropAfter = 600_000;
		Service(new FileTransferSettings(), local).EnqueueDownload(QueuedWith(_fileSystem), Origin, entry);
		await Assert.ThrowsAsync<RemoteFileSystemException>(() => RunAsync(0, new Attempt()));

		await _queue.Requests[0].DiscardAsync!();

		Assert.Null(local.PartialLength);
	}

	private static LocalFileItem BigFile(byte[] content) => new()
	{
		Name = BigName,
		RelativePath = BigName,
		Length = content.Length,
		OpenReadAsync = _ => ValueTask.FromResult<Stream>(new MemoryStream(content)),
	};

	/// <summary>Puts a file on the fake server for downloading, replacing what was there with a new size and time.</summary>
	private RemoteFileEntry ServeDownload(byte[] content)
	{
		RemoteFileEntry entry = FakeFileSystem.File(Download, content.Length) with { LastModified = DateTimeOffset.UnixEpoch.AddSeconds(content.Length) };
		_ = _fileSystem.Contains("/srv", entry);
		_fileSystem.Contents[Download] = content;
		return entry;
	}

	private RemoteTransferService Service(FileTransferSettings settings, ILocalFileAccess? localFiles = null) => new(
		_queue,
		localFiles ?? new NoLocalFiles(),
		new ThrowingInteraction(),
		new FixedSettings(settings),
		TimeProvider.System,
		NullLogger<RemoteTransferService>.Instance);

	private Task RunAsync(int index, Attempt attempt) => _queue.Requests[index].ExecuteAsync(attempt.Context());

	/// <summary>What one run of a transfer reported, recorded as it happened.</summary>
	private sealed class Attempt
	{
		public List<long> Progress { get; } = [];

		public List<TransferVerification> Verifications { get; } = [];

		public List<long> Resumed { get; } = [];

		public TransferContext Context() =>
			new(new Recorder(Progress), _ => { }, Verifications.Add, Resumed.Add, CancellationToken.None);
	}

	private sealed class Recorder(List<long> reports) : IProgress<long>
	{
		public void Report(long value) => reports.Add(value);
	}

	/// <summary>Every section at its defaults except the one the test sets.</summary>
	private sealed class FixedSettings(FileTransferSettings files) : ISettingsService
	{
		public event Action<string>? Changed
		{
			add { }
			remove { }
		}

		public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public T Get<T>()
			where T : class, ISettingsSection, new() => files as T ?? new T();

		public Task UpdateAsync<T>(Func<T, T> update, CancellationToken cancellationToken = default)
			where T : class, ISettingsSection, new() => throw new NotSupportedException();

		public Task ResetAsync<T>(CancellationToken cancellationToken = default)
			where T : class, ISettingsSection, new() => throw new NotSupportedException();

		public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
	}

	/// <summary>A host that can continue downloads, the way the desktop app does, with its files in memory.</summary>
	private sealed class MemoryLocalFiles : ILocalFileAccess
	{
		private MemoryStream? _partial;

		public byte[]? Saved { get; private set; }

		public long? PartialLength => _partial?.Length;

		public bool CanPickFolders => false;

		public bool CanResumeSaves => true;

		public async ValueTask<bool> SaveFileAsync(LocalSave save, Func<Stream, long, CancellationToken, Task> writeAsync, CancellationToken cancellationToken = default)
		{
			save.TargetPath ??= "/downloads/" + save.SuggestedName;
			if (save.PartialPath is null || _partial is null)
			{
				_partial = new MemoryStream();
				save.PartialPath = save.TargetPath + ".part";
			}

			_partial.Position = _partial.Length;
			await writeAsync(_partial, _partial.Length, cancellationToken);
			Saved = _partial.ToArray();
			_partial = null;
			save.PartialPath = null;
			return true;
		}

		public ValueTask DiscardAsync(LocalSave save)
		{
			_partial = null;
			save.PartialPath = null;
			return ValueTask.CompletedTask;
		}

		public async ValueTask<bool> SaveFileAsync(string suggestedName, long? length, Func<Stream, CancellationToken, Task> writeAsync, CancellationToken cancellationToken = default)
		{
			using MemoryStream stream = new();
			await writeAsync(stream, cancellationToken);
			Saved = stream.ToArray();
			return true;
		}

		public ValueTask<IReadOnlyList<LocalFileItem>> PickFilesAsync(bool multiple = true, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public ValueTask<IReadOnlyList<LocalFileItem>> PickFolderAsync(CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();
	}
}
