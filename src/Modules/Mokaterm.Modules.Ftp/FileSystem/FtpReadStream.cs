namespace Mokaterm.Modules.Ftp.FileSystem;

/// <summary>
/// A download stream that owns its connection. Reading to the end and disposing returns the connection for reuse; closing
/// early aborts the transfer by closing the connection, the only reliable way to stop a RETR.
/// </summary>
internal sealed class FtpReadStream : Stream
{
	private readonly Stream _inner;
	private readonly FtpClientLease _lease;
	private long _position;
	private bool _endReached;
	private bool _faulted;
	private int _disposed;

	public FtpReadStream(Stream inner, FtpClientLease lease)
	{
		_inner = inner;
		_lease = lease;
	}

	public override bool CanRead => Volatile.Read(ref _disposed) == 0;

	public override bool CanSeek => false;

	public override bool CanWrite => false;

	public override long Length => throw new NotSupportedException();

	public override long Position
	{
		get => _position;
		set => throw new NotSupportedException();
	}

	public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
		try
		{
			int read = await _inner.ReadAsync(buffer, cancellationToken);
			Track(read, buffer.Length);
			return read;
		}
		catch
		{
			_faulted = true;
			throw;
		}
	}

	public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
		ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

	public override int Read(byte[] buffer, int offset, int count)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
		try
		{
			int read = _inner.Read(buffer, offset, count);
			Track(read, count);
			return read;
		}
		catch
		{
			_faulted = true;
			throw;
		}
	}

	public override void Flush()
	{
	}

	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

	public override void SetLength(long value) => throw new NotSupportedException();

	public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

	public override async ValueTask DisposeAsync()
	{
		await CloseAsync();
		await base.DisposeAsync();
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing && Volatile.Read(ref _disposed) == 0)
		{
			// Handing the connection back needs network I/O; do it in the background rather than block the caller.
			_ = CloseAsync();
		}

		base.Dispose(disposing);
	}

	private void Track(int read, int requested)
	{
		_position += read;
		if (read == 0 && requested > 0)
		{
			_endReached = true;
		}
	}

	private async Task CloseAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		if (_endReached && !_faulted)
		{
			await _lease.ReleaseAsync(await FtpDataStreams.FinishQuietlyAsync(_inner));
			return;
		}

		await FtpDataStreams.AbandonAsync(_inner, _lease);
	}
}
