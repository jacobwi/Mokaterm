namespace Mokaterm.UI.Common.Platform;

/// <summary>
/// A picked file as a reader sees it through <see cref="LocalFilePick.ReadStreamAsync"/>: read only, no seeking, and no
/// byte past the cap. The first read that would go past it throws and every later one does too, so a reader that keeps
/// what it reads cannot hold more than the caller allowed. The stream underneath belongs to the pick and stays open.
/// </summary>
internal sealed class CappedReadStream : Stream
{
	private readonly Stream _inner;
	private readonly long _maxBytes;
	private long _read;

	public CappedReadStream(Stream inner, long maxBytes)
	{
		_inner = inner;
		_maxBytes = maxBytes;
	}

	/// <summary>True once a read went past the cap, whatever the reader did with the exception.</summary>
	public bool WentPastCap { get; private set; }

	public override bool CanRead => true;

	public override bool CanSeek => false;

	public override bool CanWrite => false;

	public override long Length => throw new NotSupportedException();

	public override long Position
	{
		get => throw new NotSupportedException();
		set => throw new NotSupportedException();
	}

	public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
	{
		ThrowIfPastCap();
		return Count(await _inner.ReadAsync(buffer, cancellationToken));
	}

	public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
		ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

	public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

	public override int Read(Span<byte> buffer)
	{
		ThrowIfPastCap();
		return Count(_inner.Read(buffer));
	}

	public override void Flush()
	{
	}

	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

	public override void SetLength(long value) => throw new NotSupportedException();

	public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

	private int Count(int read)
	{
		_read += read;
		if (_read > _maxBytes)
		{
			WentPastCap = true;
			ThrowIfPastCap();
		}

		return read;
	}

	private void ThrowIfPastCap()
	{
		if (WentPastCap)
		{
			throw new IOException("The file is larger than the reader may take.");
		}
	}
}
