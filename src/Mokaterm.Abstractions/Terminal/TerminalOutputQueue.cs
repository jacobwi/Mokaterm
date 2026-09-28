using System.Threading.Channels;

namespace Mokaterm.Abstractions.Terminal;

/// <summary>
/// The chunks a protocol's read loop hands to the terminal view, and the drain <see cref="ITerminalChannel.ReadAsync"/>
/// takes them back out through. The queue is short on purpose, like a pty buffer: a view that falls behind stops the
/// loop from reading, which pushes back on the remote side instead of buffering without limit.
/// </summary>
/// <remarks>
/// Nothing here is disposed. A read or a write still unwinding after the queue closed may use it, and it holds no
/// unmanaged handle.
/// </remarks>
public sealed class TerminalOutputQueue
{
	/// <summary>Chunks queued for the view.</summary>
	private const int Capacity = 16;

	private readonly Channel<byte[]> _output = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(Capacity)
	{
		SingleReader = true,
		FullMode = BoundedChannelFullMode.Wait,
	});

	private byte[]? _pending;
	private int _pendingOffset;

	/// <summary>
	/// Copies the next output into <paramref name="buffer"/>, waiting while nothing is queued, and returns 0 once the
	/// queue is complete and drained. One reader at a time, which is what <see cref="ITerminalChannel"/> promises.
	/// </summary>
	public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
	{
		if (buffer.IsEmpty)
		{
			return 0;
		}

		while (_pending is null)
		{
			if (_output.Reader.TryRead(out byte[]? chunk))
			{
				_pending = chunk;
				_pendingOffset = 0;
			}
			else if (!await _output.Reader.WaitToReadAsync(cancellationToken))
			{
				return 0;
			}
		}

		int count = Math.Min(buffer.Length, _pending.Length - _pendingOffset);
		_pending.AsSpan(_pendingOffset, count).CopyTo(buffer.Span);
		_pendingOffset += count;
		if (_pendingOffset == _pending.Length)
		{
			_pending = null;
		}

		return count;
	}

	/// <summary>
	/// Queues output, waiting while the view is behind. Throws <see cref="ChannelClosedException"/> once the queue is
	/// complete, which is how a read loop learns that its session ended under it.
	/// </summary>
	public ValueTask WriteAsync(byte[] chunk, CancellationToken cancellationToken) =>
		_output.Writer.WriteAsync(chunk, cancellationToken);

	/// <summary>Queues output and drops it when the queue has already closed.</summary>
	public async ValueTask WriteIfOpenAsync(byte[] chunk, CancellationToken cancellationToken)
	{
		try
		{
			await _output.Writer.WriteAsync(chunk, cancellationToken);
		}
		catch (ChannelClosedException)
		{
			// The session ended while a key was on its way in.
		}
	}

	/// <summary>No more output will arrive. A reader drains what is queued and then sees 0.</summary>
	public void Complete() => _output.Writer.TryComplete();
}
