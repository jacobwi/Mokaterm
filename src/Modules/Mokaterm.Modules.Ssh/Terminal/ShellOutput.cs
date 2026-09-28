using System.Threading.Channels;

namespace Mokaterm.Modules.Ssh.Terminal;

/// <summary>
/// Carries a shell's output from SSH.NET's blocking read to <see cref="ReadAsync"/> with memory bounded end to end.
/// SSH.NET opens the channel window again as soon as data arrives and keeps whatever the server sends in its own buffer,
/// so the only way to slow a server down is to hold up the message loop that fills that buffer. <see cref="Throttle"/>
/// does that once too much output waits unread, and TCP then pushes back on the server.
/// </summary>
internal sealed class ShellOutput
{
	/// <summary>The most one read takes off the shell.</summary>
	internal const int ChunkBytes = 32 * 1024;

	/// <summary>Chunks read off the shell and not yet taken by <see cref="ReadAsync"/>. The reader thread waits when this many are queued.</summary>
	internal const int QueueCapacity = 16;

	/// <summary>Unread output the shell may hold before <see cref="Throttle"/> holds up the thread that fills it.</summary>
	internal const long MaxBacklogBytes = 1024 * 1024;

	/// <summary>A held producer goes on once the shell holds no more than this, so it is not stopped again after every packet.</summary>
	internal const long ResumeBacklogBytes = MaxBacklogBytes / 2;

	// Only a safety net: the reader signals after every read, and Stop wakes a held producer at once.
	private static readonly TimeSpan RecheckInterval = TimeSpan.FromSeconds(1);

	private readonly Func<byte[], int> _read;
	private readonly Func<long> _backlog;

	// Not single-writer: Stop completes the queue from whichever thread disposes the channel.
	private readonly Channel<byte[]> _queue = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(QueueCapacity)
	{
		FullMode = BoundedChannelFullMode.Wait,
		SingleReader = true,
	});

	private readonly TaskCompletionSource _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);

	// Monitor rather than an event object: a held producer and the reader meet here, and nothing needs disposing.
	private readonly object _drained = new();
	private byte[]? _pending;
	private int _pendingOffset;
	private bool _finished;

	/// <param name="read">
	/// Blocks until output is available, copies it into the buffer and returns how much; 0 once the shell ended. An exception
	/// ends the output as well.
	/// </param>
	/// <param name="backlog">How many bytes the shell holds that <paramref name="read"/> has not taken yet.</param>
	/// <param name="threadName">The name of the reader thread, for debuggers and dumps.</param>
	public ShellOutput(Func<byte[], int> read, Func<long> backlog, string threadName)
	{
		_read = read;
		_backlog = backlog;
		Thread reader = new(ReadLoop)
		{
			IsBackground = true,
			Name = threadName,
		};

		reader.Start();
	}

	/// <summary>Completes once no more output will be queued: the shell ended, reading failed or <see cref="Stop"/> was called.</summary>
	public Task Ended => _ended.Task;

	/// <summary>Takes queued output in the order it was read. Returns 0 once the output ended and the queue is empty.</summary>
	public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
	{
		if (buffer.IsEmpty)
		{
			return 0;
		}

		while (_pending is null)
		{
			if (_queue.Reader.TryRead(out byte[]? chunk))
			{
				_pending = chunk;
				_pendingOffset = 0;
			}
			else if (!await _queue.Reader.WaitToReadAsync(cancellationToken))
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
	/// Called on the thread that fills the shell's buffer, after each arrival. Returns at once while little output waits;
	/// otherwise waits until the reader took the backlog down or the output stopped. Never throws.
	/// </summary>
	public void Throttle()
	{
		try
		{
			if (Backlog() <= MaxBacklogBytes)
			{
				return;
			}

			lock (_drained)
			{
				while (!_finished && Backlog() > ResumeBacklogBytes)
				{
					_ = Monitor.Wait(_drained, RecheckInterval);
				}
			}
		}
		catch (Exception)
		{
			// The caller is SSH.NET's message loop, where an exception would tear down the whole connection.
		}
	}

	/// <summary>Stops the reader and releases a producer held in <see cref="Throttle"/>. Output still unread is dropped.</summary>
	public void Stop()
	{
		_ = _queue.Writer.TryComplete();
		Finish();
	}

	private long Backlog()
	{
		try
		{
			return _backlog();
		}
		catch (Exception)
		{
			// A shell that can no longer say what it holds holds nothing worth waiting for.
			return 0;
		}
	}

	private void ReadLoop()
	{
		byte[] buffer = new byte[ChunkBytes];
		try
		{
			while (true)
			{
				int read = _read(buffer);
				if (read <= 0)
				{
					return;
				}

				// The shell's buffer just shrank, so a producer held back by the backlog may go on.
				lock (_drained)
				{
					Monitor.PulseAll(_drained);
				}

				if (!Enqueue(buffer.AsSpan(0, read).ToArray()))
				{
					return;
				}
			}
		}
		catch (Exception)
		{
			// Whatever the shell throws means no more output. An exception must not leave this thread: it would end the process.
		}
		finally
		{
			_ = _queue.Writer.TryComplete();
			Finish();
		}
	}

	/// <summary>Queues a chunk, waiting while the queue is full. False once the queue was completed.</summary>
	private bool Enqueue(byte[] chunk)
	{
		ChannelWriter<byte[]> writer = _queue.Writer;
		while (!writer.TryWrite(chunk))
		{
			// Waiting here leaves the pressure to the shell's own buffer until Throttle holds up the thread that fills it.
			if (!writer.WaitToWriteAsync().AsTask().GetAwaiter().GetResult())
			{
				return false;
			}
		}

		return true;
	}

	private void Finish()
	{
		lock (_drained)
		{
			_finished = true;
			Monitor.PulseAll(_drained);
		}

		_ = _ended.TrySetResult();
	}
}
