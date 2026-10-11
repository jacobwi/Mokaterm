using System.Security.Cryptography;

namespace Mokaterm.UI.FileBrowser.Transfers;

/// <summary>
/// Passes bytes to or from another stream and hashes them on the way: what an upload reads from its source, or what a
/// download writes to its destination. It cannot seek, so whoever skips part of it reads that part, and the hash still
/// covers the whole file. The inner stream and the hash belong to the caller and outlive this one.
/// </summary>
internal sealed class HashingStream : Stream
{
	private readonly Stream _inner;
	private readonly IncrementalHash _hash;

	public HashingStream(Stream inner, IncrementalHash hash)
	{
		_inner = inner;
		_hash = hash;
	}

	public override bool CanRead => _inner.CanRead;

	public override bool CanSeek => false;

	public override bool CanWrite => _inner.CanWrite;

	public override long Length => throw new NotSupportedException();

	public override long Position
	{
		get => throw new NotSupportedException();
		set => throw new NotSupportedException();
	}

	public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
	{
		int read = await _inner.ReadAsync(buffer, cancellationToken);
		_hash.AppendData(buffer.Span[..read]);
		return read;
	}

	public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
		ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

	public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

	public override int Read(Span<byte> buffer)
	{
		int read = _inner.Read(buffer);
		_hash.AppendData(buffer[..read]);
		return read;
	}

	public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
	{
		await _inner.WriteAsync(buffer, cancellationToken);
		_hash.AppendData(buffer.Span);
	}

	public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
		WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

	public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

	public override void Write(ReadOnlySpan<byte> buffer)
	{
		_inner.Write(buffer);
		_hash.AppendData(buffer);
	}

	public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

	public override void Flush() => _inner.Flush();

	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

	public override void SetLength(long value) => throw new NotSupportedException();
}
