namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// A file inside a container, read a piece at a time: each piece is one docker exec, so the file is never held whole,
/// and a piece that comes back shorter than asked is the end of the file. Pieces start small, since a preview or the
/// editor reads only the start, and grow to <see cref="MaxPieceBytes"/> for a download, where every exec costs a round
/// trip to the daemon.
/// </summary>
/// <remarks>
/// The exec channels the module has carry no stdin as the login and no flow control, so this is what streaming looks
/// like here: one bounded command after another, each under the connection's exec budget. A file that changes between
/// pieces reads as whatever each piece found.
/// </remarks>
internal sealed class ContainerReadStream : Stream
{
	public const int FirstPieceBytes = 256 * 1024;

	public const int MaxPieceBytes = 4 * 1024 * 1024;

	private readonly Func<long, int, CancellationToken, Task<ReadOnlyMemory<byte>>> _readPiece;
	private long _next;
	private int _pieceBytes = FirstPieceBytes;
	private ReadOnlyMemory<byte> _pending;
	private bool _ended;
	private int _disposed;

	private ContainerReadStream(Func<long, int, CancellationToken, Task<ReadOnlyMemory<byte>>> readPiece, long start)
	{
		_readPiece = readPiece;
		_next = start;
	}

	public override bool CanRead => Volatile.Read(ref _disposed) == 0;

	public override bool CanSeek => false;

	public override bool CanWrite => false;

	public override long Length => throw new NotSupportedException();

	public override long Position
	{
		get => throw new NotSupportedException();
		set => throw new NotSupportedException();
	}

	/// <summary>
	/// Reads the first piece before returning, so a file that is missing, a folder or not readable fails here, where a
	/// caller opening a file expects it, rather than at the first read.
	/// </summary>
	/// <param name="readPiece">Reads up to the given count of bytes from the given offset; fewer means the file ends.</param>
	/// <param name="start">The byte to start at, for a download that continues.</param>
	public static async Task<ContainerReadStream> OpenAsync(
		Func<long, int, CancellationToken, Task<ReadOnlyMemory<byte>>> readPiece,
		long start,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(readPiece);
		ArgumentOutOfRangeException.ThrowIfNegative(start);
		ContainerReadStream stream = new(readPiece, start);
		await stream.FillAsync(cancellationToken);
		return stream;
	}

	public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
		if (buffer.IsEmpty)
		{
			return 0;
		}

		if (_pending.IsEmpty)
		{
			if (_ended)
			{
				return 0;
			}

			await FillAsync(cancellationToken);
			if (_pending.IsEmpty)
			{
				return 0;
			}
		}

		int count = Math.Min(buffer.Length, _pending.Length);
		_pending[..count].CopyTo(buffer);
		_pending = _pending[count..];
		return count;
	}

	public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
		ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

	// Every piece is an exec on the connection, which only has an async API. Run off the caller's context, so a caller on
	// a renderer's thread does not wait on a continuation that needs that very thread.
	public override int Read(byte[] buffer, int offset, int count) =>
		Task.Run(() => ReadAsync(buffer.AsMemory(offset, count)).AsTask()).GetAwaiter().GetResult();

	public override void Flush()
	{
	}

	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

	public override void SetLength(long value) => throw new NotSupportedException();

	public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

	protected override void Dispose(bool disposing)
	{
		if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
		{
			_pending = ReadOnlyMemory<byte>.Empty;
		}

		base.Dispose(disposing);
	}

	private async Task FillAsync(CancellationToken cancellationToken)
	{
		int asked = _pieceBytes;
		ReadOnlyMemory<byte> piece = await _readPiece(_next, asked, cancellationToken);
		if (piece.Length > asked)
		{
			piece = piece[..asked];
		}

		_next += piece.Length;
		_ended = piece.Length < asked;
		_pending = piece;
		_pieceBytes = Math.Min(_pieceBytes * 4, MaxPieceBytes);
	}
}
