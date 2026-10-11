using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.Core.Tests.Abstractions;

/// <summary>Getting past what an earlier attempt already moved, and the default resumed download built on it.</summary>
public sealed class TransferStreamsTests
{
	private static readonly byte[] Content = [.. Enumerable.Range(0, 1000).Select(value => (byte)value)];

	[Fact]
	public async Task Skip_OnASeekableStream_Seeks()
	{
		using MemoryStream source = new(Content);

		Assert.True(await TransferStreams.TrySkipAsync(source, 600, TestContext.Current.CancellationToken));
		Assert.Equal(600, source.Position);
	}

	// A stream that hashes what passes through it cannot seek, and must still see every byte.
	[Fact]
	public async Task Skip_OnAStreamThatCannotSeek_ReadsThePart()
	{
		ForwardOnly source = new(Content);

		Assert.True(await TransferStreams.TrySkipAsync(source, 600, TestContext.Current.CancellationToken));
		Assert.Equal(600, source.Consumed);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task Skip_PastTheEnd_IsRefused(bool seekable)
	{
		Stream source = seekable ? new MemoryStream(Content) : new ForwardOnly(Content);

		Assert.False(await TransferStreams.TrySkipAsync(source, 1001, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task DefaultDownload_FromAnOffset_WritesTheRestAndCountsFromThere()
	{
		IRemoteFileSystem files = new ReadOnlyFiles(Content);
		using MemoryStream received = new();
		List<long> reports = [];

		await files.DownloadAsync("/srv/data.bin", received, 750, new Recorder(reports), TestContext.Current.CancellationToken);

		Assert.Equal(Content[750..], received.ToArray());
		Assert.Equal(250, reports.Last());
	}

	[Fact]
	public async Task DefaultDownload_FromTheStart_IsThePlainDownload()
	{
		ReadOnlyFiles files = new(Content);

		await ((IRemoteFileSystem)files).DownloadAsync("/srv/data.bin", new MemoryStream(), 0, null, TestContext.Current.CancellationToken);

		Assert.True(files.PlainDownloads == 1);
	}

	[Fact]
	public async Task DefaultDownload_OfAFileShorterThanTheOffset_SaysItChanged()
	{
		IRemoteFileSystem files = new ReadOnlyFiles(Content);

		RemoteFileSystemException failure = await Assert.ThrowsAsync<RemoteFileSystemException>(async () =>
			await files.DownloadAsync("/srv/data.bin", new MemoryStream(), 5000, null, TestContext.Current.CancellationToken));

		Assert.Equal("/srv/data.bin", failure.Path);
	}

	[Fact]
	public void Resume_RecordsAndForgetsItsPartialFile()
	{
		List<long> resumed = [];
		UploadResume resume = new(resumed.Add);

		resume.Record("/srv/.mokaterm-0123456789abcdef.part", isTemporary: true);
		resume.NotifyResumed(4096);
		string? recorded = resume.PartialPath;
		resume.Clear();

		Assert.Equal("/srv/.mokaterm-0123456789abcdef.part", recorded);
		Assert.Equal([4096], resumed);
		Assert.Null(resume.PartialPath);
		Assert.False(resume.IsTemporary);
	}

	private sealed class Recorder(List<long> reports) : IProgress<long>
	{
		public void Report(long value) => reports.Add(value);
	}

	private sealed class ForwardOnly(byte[] content) : Stream
	{
		private readonly MemoryStream _inner = new(content);

		public long Consumed => _inner.Position;

		public override bool CanRead => true;

		public override bool CanSeek => false;

		public override bool CanWrite => false;

		public override long Length => throw new NotSupportedException();

		public override long Position
		{
			get => throw new NotSupportedException();
			set => throw new NotSupportedException();
		}

		public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, Math.Min(count, 100));

		public override void Flush()
		{
		}

		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

		public override void SetLength(long value) => throw new NotSupportedException();

		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				_inner.Dispose();
			}

			base.Dispose(disposing);
		}
	}

	/// <summary>One file that can only be read, implementing nothing a default member would cover.</summary>
	private sealed class ReadOnlyFiles(byte[] content) : IRemoteFileSystem
	{
		public int PlainDownloads { get; private set; }

		public RemoteFileSystemFeatures Features => RemoteFileSystemFeatures.None;

		public string UserName => "abc";

		public bool IsElevated => false;

		public ValueTask<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default) =>
			ValueTask.FromResult<Stream>(new ForwardOnly(content));

		public async ValueTask DownloadAsync(string path, Stream destination, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
		{
			PlainDownloads++;
			await destination.WriteAsync(content, cancellationToken);
		}

		public ValueTask<string> GetHomeDirectoryAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public ValueTask<string> ResolvePathAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public ValueTask<IReadOnlyList<RemoteFileEntry>> ListAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public ValueTask<RemoteFileEntry?> StatAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public ValueTask CreateDirectoryAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public ValueTask DeleteAsync(string path, bool recursive, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public ValueTask RenameAsync(string sourcePath, string destinationPath, bool overwrite, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public ValueTask SetPermissionsAsync(string path, UnixFileMode permissions, bool recursive, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public ValueTask SetOwnerAsync(string path, string owner, string? group, bool recursive, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public ValueTask UploadAsync(string path, Stream source, UploadOptions options, IProgress<long>? progress = null, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}
}
