using System.Threading.Channels;

namespace Mokaterm.Modules.Serial.Tests.Fakes;

/// <summary>
/// The two directions of a serial line, without a serial port: what the test sends arrives at
/// <see cref="ReadAsync"/>, and what the channel writes lands in <see cref="Written"/>. Timeouts and a device that
/// goes away can be asked for, which is how the failure paths are exercised.
/// </summary>
internal sealed class FakeSerialStream : Stream
{
	private readonly Channel<byte[]> _inbound = Channel.CreateUnbounded<byte[]>();
	private readonly Channel<int> _writes = Channel.CreateUnbounded<int>();
	private readonly List<byte> _written = [];
	private readonly Lock _gate = new();

	private byte[]? _pending;
	private int _offset;
	private Exception? _readFailure;

	public override bool CanRead => true;

	public override bool CanSeek => false;

	public override bool CanWrite => true;

	public override long Length => throw new NotSupportedException();

	public override long Position
	{
		get => throw new NotSupportedException();
		set => throw new NotSupportedException();
	}

	/// <summary>Set to make the next read report a timeout, the way a port with a read timeout does.</summary>
	public bool TimeoutNextRead { get; set; }

	/// <summary>Set to make every write report a timeout, the way a device holding the line does.</summary>
	public bool TimeoutWrites { get; set; }

	/// <summary>Set to make every write fail outright.</summary>
	public Exception? WriteFailure { get; set; }

	public int Flushes { get; private set; }

	/// <summary>Everything the channel has written, in order.</summary>
	public byte[] Written
	{
		get
		{
			lock (_gate)
			{
				return [.. _written];
			}
		}
	}

	/// <summary>Queues bytes as if the device had sent them.</summary>
	public void Send(params byte[] data) => _inbound.Writer.TryWrite(data);

	/// <summary>Ends the line cleanly: a pending read returns 0.</summary>
	public void CloseLine() => _inbound.Writer.TryComplete();

	/// <summary>Ends the line the way an unplugged adapter does: a pending read throws.</summary>
	public void Unplug(Exception? failure = null)
	{
		_readFailure = failure ?? new IOException("The device is not connected.");
		_inbound.Writer.TryComplete();
	}

	/// <summary>Waits until at least <paramref name="count"/> bytes have been written.</summary>
	public async Task<byte[]> WaitForWrittenAsync(int count, CancellationToken cancellationToken)
	{
		while (true)
		{
			byte[] written = Written;
			if (written.Length >= count)
			{
				return written;
			}

			await _writes.Reader.ReadAsync(cancellationToken);
		}
	}

	public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
	{
		if (TimeoutNextRead)
		{
			TimeoutNextRead = false;
			throw new TimeoutException("The read timed out.");
		}

		while (_pending is null)
		{
			if (_inbound.Reader.TryRead(out byte[]? chunk))
			{
				_pending = chunk;
				_offset = 0;
			}
			else if (!await _inbound.Reader.WaitToReadAsync(cancellationToken))
			{
				return _readFailure is { } failure ? throw failure : 0;
			}
		}

		int count = Math.Min(buffer.Length, _pending.Length - _offset);
		_pending.AsSpan(_offset, count).CopyTo(buffer.Span);
		_offset += count;
		if (_offset == _pending.Length)
		{
			_pending = null;
		}

		return count;
	}

	public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
		ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

	public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
	{
		if (TimeoutWrites)
		{
			throw new TimeoutException("The write timed out.");
		}

		if (WriteFailure is { } failure)
		{
			throw failure;
		}

		lock (_gate)
		{
			_written.AddRange(buffer.Span);
		}

		_writes.Writer.TryWrite(buffer.Length);
		return ValueTask.CompletedTask;
	}

	public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
		WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

	public override void Flush() => Flushes++;

	public override Task FlushAsync(CancellationToken cancellationToken)
	{
		Flush();
		return Task.CompletedTask;
	}

	public override int Read(byte[] buffer, int offset, int count) =>
		ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();

	public override void Write(byte[] buffer, int offset, int count) =>
		WriteAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();

	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

	public override void SetLength(long value) => throw new NotSupportedException();
}
