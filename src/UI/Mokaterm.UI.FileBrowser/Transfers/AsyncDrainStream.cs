using System.Buffers;

namespace Mokaterm.UI.FileBrowser.Transfers;

/// <summary>
/// A write-only wrapper that holds synchronous writes in memory and sends them ahead of the next asynchronous write.
/// ZipArchive writes entry headers and the central directory synchronously, which an ASP.NET Core response body
/// refuses. Those writes are small; file content goes through <see cref="WriteAsync(ReadOnlyMemory{byte}, CancellationToken)"/>.
/// </summary>
internal sealed class AsyncDrainStream : Stream
{
	private readonly Stream _inner;
	private readonly ArrayBufferWriter<byte> _pending = new();
	private long _position;

	public AsyncDrainStream(Stream inner) => _inner = inner;

	public override bool CanRead => false;

	public override bool CanSeek => false;

	public override bool CanWrite => true;

	public override long Length => throw new NotSupportedException();

	public override long Position
	{
		get => _position;
		set => throw new NotSupportedException();
	}

	public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

	public override void Write(ReadOnlySpan<byte> buffer)
	{
		_pending.Write(buffer);
		_position += buffer.Length;
	}

	public override void WriteByte(byte value) => Write([value]);

	public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
		WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

	public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
	{
		await DrainAsync(cancellationToken);
		await _inner.WriteAsync(buffer, cancellationToken);
		_position += buffer.Length;
	}

	/// <summary>Sends the bytes held back from synchronous writes.</summary>
	public async ValueTask DrainAsync(CancellationToken cancellationToken)
	{
		if (_pending.WrittenCount == 0)
		{
			return;
		}

		await _inner.WriteAsync(_pending.WrittenMemory, cancellationToken);
		_pending.ResetWrittenCount();
	}

	// A synchronous flush cannot reach the inner stream without blocking; FlushAsync drains and flushes it.
	public override void Flush()
	{
	}

	public override async Task FlushAsync(CancellationToken cancellationToken)
	{
		await DrainAsync(cancellationToken);
		await _inner.FlushAsync(cancellationToken);
	}

	public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

	public override void SetLength(long value) => throw new NotSupportedException();
}
