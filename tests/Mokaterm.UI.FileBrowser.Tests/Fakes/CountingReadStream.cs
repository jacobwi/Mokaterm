namespace Mokaterm.UI.FileBrowser.Tests.Fakes;

/// <summary>A read-only stream that reports every byte it hands out, so a test can hold a reader to its cap.</summary>
internal sealed class CountingReadStream(Stream inner, Action<int> counted) : Stream
{
	public override bool CanRead => true;

	public override bool CanSeek => false;

	public override bool CanWrite => false;

	public override long Length => throw new NotSupportedException();

	public override long Position
	{
		get => throw new NotSupportedException();
		set => throw new NotSupportedException();
	}

	public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));

	public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return Count(await inner.ReadAsync(buffer, cancellationToken));
	}

	public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
		ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

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
			inner.Dispose();
		}

		base.Dispose(disposing);
	}

	private int Count(int read)
	{
		counted(read);
		return read;
	}
}
