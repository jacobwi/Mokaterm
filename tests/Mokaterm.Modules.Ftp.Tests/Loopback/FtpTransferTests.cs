using System.Security.Cryptography;
using System.Text;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Ftp.Tests.Fakes;

namespace Mokaterm.Modules.Ftp.Tests.Loopback;

public sealed class FtpTransferTests
{
	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task UploadThenDownload_RoundTripsContentWithCumulativeProgress()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackFtpServer server = FtpHarness.StartServer();
		await using IProtocolSession session = await FtpHarness.ConnectAsync(server, cancellationToken);
		IRemoteFileSystem files = await FtpHarness.OpenFileSystemAsync(session, cancellationToken);
		byte[] payload = RandomNumberGenerator.GetBytes(300_000);
		RecordingProgress<long> uploadProgress = new();
		RecordingProgress<long> downloadProgress = new();

		await files.UploadAsync("/home/alice/upload.bin", new MemoryStream(payload), UploadOptions.Default, uploadProgress, cancellationToken);
		using MemoryStream downloaded = new();
		await files.DownloadAsync("/home/alice/upload.bin", downloaded, downloadProgress, cancellationToken);

		Assert.Equal(payload, server.Files.Get("/home/alice/upload.bin")?.Content);
		Assert.Equal(payload, downloaded.ToArray());
		Assert.Equal(payload.Length, uploadProgress.Reports.Last());
		Assert.Equal(payload.Length, downloadProgress.Reports.Last());
		Assert.Equal(uploadProgress.Reports.Order(), uploadProgress.Reports);
		Assert.Equal(downloadProgress.Reports.Order(), downloadProgress.Reports);
	}

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task Upload_FromAStreamThatCannotSeek_Works()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackFtpServer server = FtpHarness.StartServer();
		await using IProtocolSession session = await FtpHarness.ConnectAsync(server, cancellationToken);
		IRemoteFileSystem files = await FtpHarness.OpenFileSystemAsync(session, cancellationToken);
		byte[] payload = RandomNumberGenerator.GetBytes(100_000);

		await files.UploadAsync("/home/alice/piped.bin", new ForwardOnlyStream(payload), UploadOptions.Default, null, cancellationToken);

		Assert.Equal(payload, server.Files.Get("/home/alice/piped.bin")?.Content);
	}

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task Upload_OverExistingFile_NeedsOverwrite()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackFtpServer server = FtpHarness.StartServer();
		await using IProtocolSession session = await FtpHarness.ConnectAsync(server, cancellationToken);
		IRemoteFileSystem files = await FtpHarness.OpenFileSystemAsync(session, cancellationToken);

		RemoteFileSystemException exists = await Assert.ThrowsAsync<RemoteFileSystemException>(() =>
			files.UploadAsync(FtpHarness.NotesPath, new MemoryStream([1, 2, 3]), UploadOptions.Default, null, cancellationToken).AsTask());
		await files.UploadAsync(FtpHarness.NotesPath, new MemoryStream([1, 2, 3]), new UploadOptions { Overwrite = true }, null, cancellationToken);

		Assert.Equal(RemoteFileErrorKind.AlreadyExists, exists.Kind);
		Assert.Equal([1, 2, 3], server.Files.Get(FtpHarness.NotesPath)?.Content);
	}

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task Upload_IntoMissingFolder_IsRefused()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackFtpServer server = FtpHarness.StartServer();
		await using IProtocolSession session = await FtpHarness.ConnectAsync(server, cancellationToken);
		IRemoteFileSystem files = await FtpHarness.OpenFileSystemAsync(session, cancellationToken);

		RemoteFileSystemException exception = await Assert.ThrowsAsync<RemoteFileSystemException>(() =>
			files.UploadAsync("/home/alice/nope/file.bin", new MemoryStream([1]), UploadOptions.Default, null, cancellationToken).AsTask());

		Assert.Equal(RemoteFileErrorKind.PermissionDenied, exception.Kind);
		Assert.Contains("553", exception.Message, StringComparison.Ordinal);
	}

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task Upload_AppliesTimestampAndPermissionsWhenTheServerSupportsThem()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackFtpServer server = FtpHarness.StartServer(new LoopbackFtpServerOptions { SupportsMfmt = true });
		await using IProtocolSession session = await FtpHarness.ConnectAsync(server, cancellationToken);
		IRemoteFileSystem files = await FtpHarness.OpenFileSystemAsync(session, cancellationToken);
		DateTimeOffset stamp = new(2024, 2, 3, 4, 5, 6, TimeSpan.FromHours(2));

		await files.UploadAsync("/home/alice/stamped.txt", new MemoryStream([42]), new UploadOptions { LastModified = stamp, Permissions = FakeFileTree.Mode("640") }, null, cancellationToken);

		FakeNode? node = server.Files.Get("/home/alice/stamped.txt");
		Assert.Equal(stamp.UtcDateTime, node?.Modified);
		Assert.Equal(FakeFileTree.Mode("640"), node?.Mode);
	}

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task Upload_WithoutMfmt_DoesNotTryToSetTheTime()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackFtpServer server = FtpHarness.StartServer();
		await using IProtocolSession session = await FtpHarness.ConnectAsync(server, cancellationToken);
		IRemoteFileSystem files = await FtpHarness.OpenFileSystemAsync(session, cancellationToken);

		await files.UploadAsync("/home/alice/plain.txt", new MemoryStream([42]), new UploadOptions { LastModified = DateTimeOffset.UtcNow }, null, cancellationToken);

		Assert.DoesNotContain(server.Commands, command => command.StartsWith("MFMT", StringComparison.Ordinal) || command.StartsWith("MDTM 2", StringComparison.Ordinal));
	}

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task Upload_ServerThatStopsReading_FailsInsteadOfWaitingForever()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		FakeSettingsService settings = new();
		settings.Set(new FtpSettings { DataTimeoutSeconds = 2 });
		await using LoopbackFtpServer server = FtpHarness.StartServer(new LoopbackFtpServerOptions { StallUploads = true });
		await using IProtocolSession session = await FtpHarness.ConnectAsync(server, cancellationToken, FtpHarness.CreateProvider(settings));
		IRemoteFileSystem files = await FtpHarness.OpenFileSystemAsync(session, cancellationToken);

		// Far more than the socket buffers on both ends hold, so the writes have to wait for the server.
		byte[] payload = new byte[64 * 1024 * 1024];
		Task upload = files.UploadAsync("/home/alice/stuck.bin", new MemoryStream(payload), UploadOptions.Default, null, cancellationToken).AsTask();
		Task first = await Task.WhenAny(upload, Task.Delay(TimeSpan.FromSeconds(30), cancellationToken));

		Assert.True(first == upload, "The upload was still waiting on a server that stopped reading.");
		RemoteFileSystemException failure = await Assert.ThrowsAsync<RemoteFileSystemException>(() => upload);
		Assert.Equal(RemoteFileErrorKind.ConnectionLost, failure.Kind);
	}

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task Download_ServerThatStopsSending_FailsAfterTheDataTimeout()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		FakeSettingsService settings = new();
		settings.Set(new FtpSettings { DataTimeoutSeconds = 2 });
		await using LoopbackFtpServer server = FtpHarness.StartServer(new LoopbackFtpServerOptions { StallDownloads = true });
		server.Files.AddFile("/home/alice/big.bin", RandomNumberGenerator.GetBytes(1_000_000));
		await using IProtocolSession session = await FtpHarness.ConnectAsync(server, cancellationToken, FtpHarness.CreateProvider(settings));
		IRemoteFileSystem files = await FtpHarness.OpenFileSystemAsync(session, cancellationToken);

		Task download = files.DownloadAsync("/home/alice/big.bin", new MemoryStream(), null, cancellationToken).AsTask();
		Task first = await Task.WhenAny(download, Task.Delay(TimeSpan.FromSeconds(30), cancellationToken));

		Assert.True(first == download, "The download was still waiting on a server that stopped sending.");
		RemoteFileSystemException failure = await Assert.ThrowsAsync<RemoteFileSystemException>(() => download);
		Assert.Equal(RemoteFileErrorKind.ConnectionLost, failure.Kind);
	}

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task Download_MissingFile_ThrowsNotFound()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackFtpServer server = FtpHarness.StartServer();
		await using IProtocolSession session = await FtpHarness.ConnectAsync(server, cancellationToken);
		IRemoteFileSystem files = await FtpHarness.OpenFileSystemAsync(session, cancellationToken);

		RemoteFileSystemException exception = await Assert.ThrowsAsync<RemoteFileSystemException>(() =>
			files.DownloadAsync("/home/alice/missing.bin", new MemoryStream(), null, cancellationToken).AsTask());

		Assert.Equal(RemoteFileErrorKind.NotFound, exception.Kind);
	}

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task OpenRead_ReadToTheEnd_ReturnsTheConnectionForReuse()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackFtpServer server = FtpHarness.StartServer();
		await using IProtocolSession session = await FtpHarness.ConnectAsync(server, cancellationToken);
		IRemoteFileSystem files = await FtpHarness.OpenFileSystemAsync(session, cancellationToken);

		await using (Stream stream = await files.OpenReadAsync(FtpHarness.NotesPath, cancellationToken))
		{
			using StreamReader reader = new(stream, Encoding.UTF8, leaveOpen: true);
			Assert.Equal(FtpHarness.NotesText, await reader.ReadToEndAsync(cancellationToken));
		}

		int connections = server.TotalConnections;
		await files.DownloadAsync(FtpHarness.NotesPath, new MemoryStream(), null, cancellationToken);

		Assert.Equal(connections, server.TotalConnections);
	}

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task OpenRead_ClosedEarly_AbortsTheTransferAndLaterTransfersStillWork()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackFtpServer server = FtpHarness.StartServer();
		server.Files.AddFile("/home/alice/big.bin", RandomNumberGenerator.GetBytes(4_000_000));
		await using IProtocolSession session = await FtpHarness.ConnectAsync(server, cancellationToken);
		IRemoteFileSystem files = await FtpHarness.OpenFileSystemAsync(session, cancellationToken);

		await using (Stream stream = await files.OpenReadAsync("/home/alice/big.bin", cancellationToken))
		{
			byte[] buffer = new byte[64];
			Assert.Equal(64, await stream.ReadAtLeastAsync(buffer, 64, cancellationToken: cancellationToken));
			Assert.False(stream.CanSeek);
		}

		using MemoryStream copy = new();
		await files.DownloadAsync(FtpHarness.NotesPath, copy, null, cancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
		Assert.Equal(FtpHarness.NotesText, Encoding.UTF8.GetString(copy.ToArray()));
	}

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task Download_Cancelled_ThrowsCancellationAndLaterTransfersStillWork()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackFtpServer server = FtpHarness.StartServer();
		server.Files.AddFile("/home/alice/big.bin", RandomNumberGenerator.GetBytes(4_000_000));
		await using IProtocolSession session = await FtpHarness.ConnectAsync(server, cancellationToken);
		IRemoteFileSystem files = await FtpHarness.OpenFileSystemAsync(session, cancellationToken);
		using CancellationTokenSource cancel = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			files.DownloadAsync("/home/alice/big.bin", new CancelOnWriteStream(cancel), null, cancel.Token).AsTask());

		using MemoryStream copy = new();
		await files.DownloadAsync(FtpHarness.NotesPath, copy, null, cancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
		Assert.Equal(FtpHarness.NotesText, Encoding.UTF8.GetString(copy.ToArray()));
	}

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task Transfers_UseTheirOwnConnection_SoBrowsingIsNotBlocked()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackFtpServer server = FtpHarness.StartServer();
		server.Files.AddFile("/home/alice/big.bin", RandomNumberGenerator.GetBytes(4_000_000));
		await using IProtocolSession session = await FtpHarness.ConnectAsync(server, cancellationToken);
		IRemoteFileSystem files = await FtpHarness.OpenFileSystemAsync(session, cancellationToken);

		await using Stream open = await files.OpenReadAsync("/home/alice/big.bin", cancellationToken);
		IReadOnlyList<RemoteFileEntry> entries = await files.ListAsync(FtpHarness.Home, cancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

		Assert.Contains(entries, entry => entry.Name == "big.bin");
		Assert.Equal(2, server.OpenConnections);
	}

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task Transfers_WhenTheServerAllowsOneConnection_ShareTheBrowsingConnection()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackFtpServer server = FtpHarness.StartServer(new LoopbackFtpServerOptions { MaxConnections = 1 });
		await using IProtocolSession session = await FtpHarness.ConnectAsync(server, cancellationToken);
		IRemoteFileSystem files = await FtpHarness.OpenFileSystemAsync(session, cancellationToken);

		using MemoryStream first = new();
		await files.DownloadAsync(FtpHarness.NotesPath, first, null, cancellationToken);
		await files.UploadAsync("/home/alice/shared.txt", new MemoryStream("shared"u8.ToArray()), UploadOptions.Default, null, cancellationToken);
		IReadOnlyList<RemoteFileEntry> entries = await files.ListAsync(FtpHarness.Home, cancellationToken);

		Assert.Equal(FtpHarness.NotesText, Encoding.UTF8.GetString(first.ToArray()));
		Assert.Contains(entries, entry => entry.Name == "shared.txt");
		await Eventually.TrueAsync(() => server.OpenConnections == 1, "the transfer connection is back in the pool");
	}

	/// <summary>A readable stream without Length or Seek, like a browser upload.</summary>
	private sealed class ForwardOnlyStream(byte[] content) : Stream
	{
		private int _position;

		public override bool CanRead => true;

		public override bool CanSeek => false;

		public override bool CanWrite => false;

		public override long Length => throw new NotSupportedException();

		public override long Position
		{
			get => throw new NotSupportedException();
			set => throw new NotSupportedException();
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			int read = Math.Min(Math.Min(count, 7000), content.Length - _position);
			Array.Copy(content, _position, buffer, offset, read);
			_position += read;
			return read;
		}

		public override void Flush()
		{
		}

		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

		public override void SetLength(long value) => throw new NotSupportedException();

		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	}

	/// <summary>Cancels the download on the first chunk it receives.</summary>
	private sealed class CancelOnWriteStream(CancellationTokenSource cancel) : Stream
	{
		public override bool CanRead => false;

		public override bool CanSeek => false;

		public override bool CanWrite => true;

		public override long Length => throw new NotSupportedException();

		public override long Position
		{
			get => throw new NotSupportedException();
			set => throw new NotSupportedException();
		}

		public override void Write(byte[] buffer, int offset, int count) => cancel.Cancel();

		public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
		{
			cancel.Cancel();
			return ValueTask.CompletedTask;
		}

		public override void Flush()
		{
		}

		public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

		public override void SetLength(long value) => throw new NotSupportedException();
	}
}
