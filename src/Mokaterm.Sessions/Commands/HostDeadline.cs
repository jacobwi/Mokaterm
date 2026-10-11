namespace Mokaterm.Sessions.Commands;

/// <summary>
/// The time one host of a command run gets, from the start of its connect to the end of its command. The clock stops
/// while a prompt for that host is open, because a user reading a host key is not a slow host. The token also ends
/// with the batch the host was started in, which is how cancelling the run reaches it.
/// </summary>
internal sealed class HostDeadline : IDisposable
{
	private readonly TimeProvider _time;
	private readonly CancellationTokenSource _timer;
	private readonly CancellationTokenSource _linked;
	private readonly Lock _lock = new();
	private TimeSpan _remaining;
	private long _runningSince;
	private int _pauses;
	private bool _disposed;

	public HostDeadline(TimeSpan timeout, TimeProvider time, CancellationToken batchToken)
	{
		_time = time;
		_remaining = timeout;
		_runningSince = time.GetTimestamp();
		_timer = new CancellationTokenSource(timeout, time);
		_linked = CancellationTokenSource.CreateLinkedTokenSource(batchToken, _timer.Token);
	}

	public CancellationToken Token => _linked.Token;

	/// <summary>True once the time ran out, as opposed to the run being cancelled.</summary>
	public bool TimedOut => _timer.IsCancellationRequested;

	/// <summary>Stops the clock until the returned handle is disposed. Pauses nest.</summary>
	public IDisposable Pause()
	{
		lock (_lock)
		{
			if (_pauses++ == 0 && !_disposed && !_timer.IsCancellationRequested)
			{
				_remaining -= _time.GetElapsedTime(_runningSince);
				_timer.CancelAfter(Timeout.InfiniteTimeSpan);
			}
		}

		return new Resumption(this);
	}

	public void Dispose()
	{
		lock (_lock)
		{
			_disposed = true;
		}

		_linked.Dispose();
		_timer.Dispose();
	}

	private void Resume()
	{
		bool expired = false;
		lock (_lock)
		{
			if (--_pauses > 0 || _disposed || _timer.IsCancellationRequested)
			{
				return;
			}

			_runningSince = _time.GetTimestamp();
			if (_remaining > TimeSpan.Zero)
			{
				_timer.CancelAfter(_remaining);
			}
			else
			{
				expired = true;
			}
		}

		// Cancelling runs the token's callbacks right here, so it happens outside the lock.
		if (expired)
		{
			try
			{
				_timer.Cancel();
			}
			catch (ObjectDisposedException)
			{
				// The host finished while its prompt was closing.
			}
		}
	}

	private sealed class Resumption(HostDeadline deadline) : IDisposable
	{
		private int _done;

		public void Dispose()
		{
			if (Interlocked.Exchange(ref _done, 1) == 0)
			{
				deadline.Resume();
			}
		}
	}
}
