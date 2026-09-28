using System.Threading.Channels;

namespace Mokaterm.Modules.Vnc.Tests.Fakes;

/// <summary>
/// A session stream without a socket: the test pushes what the server sends, reads count how much the relay took,
/// and writes are recorded as what reached the server.
/// </summary>
internal sealed class FakeSessionStream : Stream
{
	private readonly Channel<byte[]> _fromServer = Channel.CreateUnbounded<byte[]>();
	private readonly List<byte> _toServer = [];
	private readonly Lock _gate = new();
	private ReadOnlyMemory<byte> _pending;
	private bool _ended;

	public override bool CanRead => true;

	public override bool CanSeek => false;

	public override bool CanWrite => true;

	public override long Length => throw new NotSupportedException();

	public override long Position
	{
		get => throw new NotSupportedException();
		set => throw new NotSupportedException();
	}

	/// <summary>How many bytes the relay has read so far.</summary>
	public int BytesRead { get; private set; }

	/// <summary>Everything the relay wrote towards the server.</summary>
	public byte[] ToServer
	{
		get
		{
			lock (_gate)
			{
				return [.. _toServer];
			}
		}
	}

	/// <summary>Queues bytes as if the server had sent them.</summary>
	public void Push(byte[] data) => _fromServer.Writer.TryWrite(data);

	/// <summary>Ends the stream, which a socket does when the server closes the connection.</summary>
	public void End() => _fromServer.Writer.TryComplete();

	public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
	{
		while (_pending.IsEmpty)
		{
			if (_ended)
			{
				return 0;
			}

			try
			{
				_pending = await _fromServer.Reader.ReadAsync(cancellationToken);
			}
			catch (ChannelClosedException)
			{
				_ended = true;
				return 0;
			}
		}

		int taken = Math.Min(buffer.Length, _pending.Length);
		_pending[..taken].CopyTo(buffer);
		_pending = _pending[taken..];
		BytesRead += taken;
		return taken;
	}

	public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
	{
		lock (_gate)
		{
			_toServer.AddRange(buffer.Span);
		}

		return ValueTask.CompletedTask;
	}

	public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

	public override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

	public override void Flush()
	{
	}

	public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

	public override void SetLength(long value) => throw new NotSupportedException();
}
