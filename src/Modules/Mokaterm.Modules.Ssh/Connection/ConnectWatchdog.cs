namespace Mokaterm.Modules.Ssh.Connection;

/// <summary>
/// Times out a connection attempt that stops making progress. SSH.NET honours cancellation only until the version
/// exchange, and its own timeout has to be long enough for a user typing a one-time code, so this watchdog restarts on
/// every step and pauses while a prompt is open.
/// </summary>
internal sealed class ConnectWatchdog : IDisposable
{
	private readonly CancellationTokenSource _source = new();
	private readonly TimeSpan _stepTimeout;
	private int _disposed;

	public ConnectWatchdog(TimeSpan stepTimeout)
	{
		_stepTimeout = stepTimeout;
		Arm();
	}

	public CancellationToken Token => _source.Token;

	public bool TimedOut => _source.IsCancellationRequested;

	/// <summary>Gives the current step a fresh timeout.</summary>
	public void Arm() => Change(_stepTimeout);

	/// <summary>Stops the clock, for example while the user answers a prompt.</summary>
	public void Suspend() => Change(Timeout.InfiniteTimeSpan);

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) == 0)
		{
			_source.Dispose();
		}
	}

	private void Change(TimeSpan delay)
	{
		if (Volatile.Read(ref _disposed) != 0)
		{
			return;
		}

		try
		{
			_source.CancelAfter(delay);
		}
		catch (ObjectDisposedException)
		{
			// SSH.NET finished the step after the attempt was abandoned and cleaned up.
		}
	}
}
