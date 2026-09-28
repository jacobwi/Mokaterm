using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Modules.Ssh.Shell;

namespace Mokaterm.Modules.Ssh.Tests;

public sealed class RemoteCommandTests
{
	private const int OneMegabyte = 1024 * 1024;

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task ReadAllAsync_ReturnsEverything_UpToTheLimit()
	{
		byte[] data = new byte[OneMegabyte];
		Random.Shared.NextBytes(data);
		using MemoryStream source = new(data);

		byte[] read = await RemoteCommand.ReadAllAsync(source, data.Length, Ct);

		Assert.Equal(data, read);
	}

	[Fact]
	public async Task ReadAllAsync_StopsOutputThatNeverEnds()
	{
		using EndlessStream source = new();

		RemoteFileSystemException failure = await Assert.ThrowsAsync<RemoteFileSystemException>(
			() => RemoteCommand.ReadAllAsync(source, OneMegabyte, Ct));

		Assert.Contains("more than 1 MB", failure.Message, StringComparison.Ordinal);
		Assert.InRange(source.Served, OneMegabyte, 2L * OneMegabyte);
	}

	/// <summary>A server that keeps printing, like <c>yes</c> behind a script.</summary>
	private sealed class EndlessStream : Stream
	{
		private long _served;

		public long Served => Interlocked.Read(ref _served);

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
			buffer.AsSpan(offset, count).Fill((byte)'y');
			_ = Interlocked.Add(ref _served, count);
			return count;
		}

		public override void Flush()
		{
		}

		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

		public override void SetLength(long value) => throw new NotSupportedException();

		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	}
}
