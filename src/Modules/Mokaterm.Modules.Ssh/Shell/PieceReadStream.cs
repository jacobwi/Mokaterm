using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.Modules.Ssh.Shell;

/// <summary>
/// A file read a piece at a time: each piece is one run of <see cref="RemoteScripts.ReadRange"/> (through sudo for the root
/// view, through docker exec for a container), so the file is never held whole whatever pace the reader keeps, and a
/// piece that comes back shorter than asked is the end of the file. Pieces start small, since a preview or the editor
/// reads only the start, and grow to <see cref="MaxPieceBytes"/> for a download, where every piece costs a round trip.
/// </summary>
/// <remarks>
/// <para>
/// Exec channels have no flow control: SSH.NET reopens the window as data arrives and keeps whatever nobody has read.
/// One bounded command after another is what streaming looks like here, and a stream never holds more than
/// <see cref="MaxPiecesHeld"/> pieces, the one being read included, however slowly it is read.
/// </para>
/// <para>
/// Once the reader has taken <see cref="WarmUpPieces"/> pieces whole it is a download, and the stream reads ahead: when
/// the reader moves on to a piece, the one after it is asked for at once, often while the first is still on its way, so
/// the channel open and sudo start of one overlap the transfer of the other. A preview never gets that far and never
/// pays for a piece it does not read. Nothing is asked for past the end: the first piece's version says how long the
/// file is, and without one the next piece is asked for only after the last came back whole.
/// </para>
/// <para>
/// Pieces belong to the stream, not to the read waiting for one: a cancelled read stops waiting and leaves its piece for
/// the next read, and disposing the stream cancels every piece still on its way. <see cref="DisposeAsync"/> returns
/// once they ended, so no exec channel or sudo outlives it. A piece's failure, or a version other than the first,
/// surfaces on the read that would take that piece and on every read after it.
/// </para>
/// <para>
/// Every piece names the version of the file it was read from, and one from another version than the first fails the
/// read: joined, the pieces would make a file that never existed. A file appended to, truncated, replaced or rewritten
/// between two pieces is refused the same way, short of a rewrite to the same size within the same second, which
/// <c>stat</c> cannot tell apart. A server that cannot say (no <c>stat</c>) is read unchecked.
/// </para>
/// </remarks>
internal sealed class PieceReadStream : Stream
{
	public const int FirstPieceBytes = 256 * 1024;

	public const int MaxPieceBytes = 4 * 1024 * 1024;

	/// <summary>The most pieces a stream holds at once, received or on their way: its memory, and its exec channels.</summary>
	public const int MaxPiecesHeld = 2;

	/// <summary>Pieces the reader takes whole before the stream reads ahead.</summary>
	public const int WarmUpPieces = 2;

	private readonly Func<long, int, CancellationToken, Task<FilePiece>> _readPiece;
	private readonly string _path;
	private readonly CancellationTokenSource _lifetime = new();
	private readonly Queue<Request> _ahead = new();
	private readonly Lock _gate = new();
	private long _nextStart;
	private int _pieceBytes = FirstPieceBytes;
	private string? _version;
	private long? _size;
	private ReadOnlyMemory<byte> _pending;
	private int _taken;
	private bool _askedAll;
	private bool _ended;
	private int _disposed;

	private PieceReadStream(Func<long, int, CancellationToken, Task<FilePiece>> readPiece, string path, long start)
	{
		_readPiece = readPiece;
		_path = path;
		_nextStart = start;
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
	/// <param name="readPiece">
	/// Reads up to the given count of bytes from the given offset; fewer means the file ends. It runs with the stream's
	/// own token, which disposing the stream cancels, and up to <see cref="MaxPiecesHeld"/> of it at once.
	/// </param>
	/// <param name="path">The file, for messages.</param>
	/// <param name="start">The byte to start at, for a download that continues.</param>
	public static async Task<PieceReadStream> OpenAsync(
		Func<long, int, CancellationToken, Task<FilePiece>> readPiece,
		string path,
		long start,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(readPiece);
		ArgumentNullException.ThrowIfNull(path);
		ArgumentOutOfRangeException.ThrowIfNegative(start);
		PieceReadStream stream = new(readPiece, path, start);
		try
		{
			await stream.MoveNextAsync(cancellationToken);
		}
		catch
		{
			// A cancelled open must not leave its piece running.
			await stream.DisposeAsync();
			throw;
		}

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

			await MoveNextAsync(cancellationToken);
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

	// Every piece is a command on the connection, which only has an async API. Run off the caller's context, so a caller
	// on a renderer's thread does not wait on a continuation that needs that very thread.
	public override int Read(byte[] buffer, int offset, int count) =>
		Task.Run(() => ReadAsync(buffer.AsMemory(offset, count)).AsTask()).GetAwaiter().GetResult();

	public override void Flush()
	{
	}

	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

	public override void SetLength(long value) => throw new NotSupportedException();

	public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

	/// <summary>Cancels the pieces still on their way and waits until they ended, channels closed and slots given back.</summary>
	public override async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) == 0)
		{
			_pending = ReadOnlyMemory<byte>.Empty;
			Task[] pieces = InFlight();
			await _lifetime.CancelAsync();
			try
			{
				await Task.WhenAll(pieces);
			}
			catch (Exception)
			{
				// Cancelled or failed: either way nobody is waiting for it any more.
			}

			_lifetime.Dispose();
		}

		await base.DisposeAsync();
	}

	/// <summary>
	/// Cancels the pieces still on their way without waiting: each ends as soon as its channel sees the cancel, which
	/// closes the channel. <see cref="DisposeAsync"/> is the one that waits.
	/// </summary>
	protected override void Dispose(bool disposing)
	{
		if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
		{
			_pending = ReadOnlyMemory<byte>.Empty;
			Task[] pieces = InFlight();
			_lifetime.Cancel();

			// The token stays valid until the last piece let go of it.
			_ = Task.WhenAll(pieces).ContinueWith(
				ended =>
				{
					_ = ended.Exception;
					_lifetime.Dispose();
				},
				CancellationToken.None,
				TaskContinuationOptions.ExecuteSynchronously,
				TaskScheduler.Default);
		}

		base.Dispose(disposing);
	}

	private static void Observe(Task task) => _ = task.ContinueWith(
		static ended => _ = ended.Exception,
		CancellationToken.None,
		TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
		TaskScheduler.Default);

	private Task[] InFlight()
	{
		lock (_gate)
		{
			return [.. _ahead.Select(request => (Task)request.Piece)];
		}
	}

	/// <summary>
	/// Makes the next piece the one being read: asks for it when nothing has yet, waits for it, checks it, and then asks
	/// ahead. A piece that fails, or fails the checks, stays first in line, so every later read says the same.
	/// </summary>
	private async Task MoveNextAsync(CancellationToken cancellationToken)
	{
		Request request;
		lock (_gate)
		{
			AskAhead();
			if (!_ahead.TryPeek(out request))
			{
				_ended = true;
				return;
			}
		}

		FilePiece piece = await request.Piece.WaitAsync(cancellationToken);
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
		lock (_gate)
		{
			Take(request, piece);
			_ = _ahead.Dequeue();
			AskAhead();
		}
	}

	/// <summary>
	/// Asks for pieces until the stream holds as many as it may: one, the piece the reader is about to read, during the
	/// warm-up, and <see cref="MaxPiecesHeld"/> after it. Never past the end the first version gave, and without one never
	/// past a piece that has not come back whole.
	/// </summary>
	private void AskAhead()
	{
		bool reading = !_pending.IsEmpty;
		int drained = _taken - (reading ? 1 : 0);
		int wanted = drained >= WarmUpPieces ? MaxPiecesHeld : 1;
		while (!_askedAll && _ahead.Count + (reading ? 1 : 0) < wanted && (_size is not null || _ahead.Count == 0) && !EndsTheRead())
		{
			Ask();
		}
	}

	/// <summary>
	/// A piece on its way already failed, or came back from another version: the read ends there, and asking past it
	/// would only cost the server another command, and with sudo refusing, another failed password.
	/// </summary>
	private bool EndsTheRead() => _ahead.Any(request => request.Piece.IsCompleted && (!request.Piece.IsCompletedSuccessfully
		|| (_version is not null && request.Piece.Result.Version.Length > 0 && !string.Equals(request.Piece.Result.Version, _version, StringComparison.Ordinal))));

	private void Ask()
	{
		long start = _nextStart;
		int asked = _pieceBytes;
		CancellationToken token = _lifetime.Token;

		// The piece is the stream's: it runs off the asking read's context and outlives that read.
		Task<FilePiece> piece = Task.Run(() => _readPiece(start, asked, token), token);

		// A piece nobody comes back for (a reader that gave up without disposing) must not fault unobserved.
		Observe(piece);
		_ahead.Enqueue(new Request(start, asked, piece));
		_nextStart = start + asked;
		_pieceBytes = Math.Min(_pieceBytes * 4, MaxPieceBytes);
		_askedAll = _size is { } size && _nextStart >= size;
	}

	private void Take(Request request, FilePiece piece)
	{
		string? version = piece.Version.Length == 0 ? null : piece.Version;
		if (version is not null && _version is not null && !string.Equals(_version, version, StringComparison.Ordinal))
		{
			throw Changed();
		}

		ReadOnlyMemory<byte> bytes = piece.Bytes.Length > request.Asked ? piece.Bytes[..request.Asked] : piece.Bytes;
		long end = request.Start + bytes.Length;
		long? size = _size ?? (version is null ? null : piece.Size);
		bool cutShort = bytes.Length < request.Asked;

		// A piece that ends before the file does was read from a file cut down meanwhile.
		if (cutShort && size is { } expected && end < expected)
		{
			throw Changed();
		}

		_version ??= version;
		_size = size;
		_pending = bytes;
		_taken++;
		_ended = cutShort || (size is { } total && end >= total);
		_askedAll |= _ended || (size is { } known && _nextStart >= known);
	}

	private RemoteFileSystemException Changed() => new(
		RemoteFileErrorKind.Unknown,
		$"{_path} changed on the server while it was being read, so the read stopped rather than join two versions of it.",
		_path);

	private readonly record struct Request(long Start, int Asked, Task<FilePiece> Piece);
}
