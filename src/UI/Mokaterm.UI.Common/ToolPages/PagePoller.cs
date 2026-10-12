namespace Mokaterm.UI.Common.ToolPages;

/// <summary>
/// The polling every tool page shares, built in or from a plugin: a tick on a fixed interval, run on the page's
/// renderer, skipped while the page says it may not run one (hidden, loading, busy with a change). Starting again
/// replaces the running poll.
/// </summary>
/// <remarks>
/// A skipped tick is not made up later: a page that comes back into view waits one interval, which keeps a hidden tool
/// from reading anything and a busy one from racing its own change.
/// </remarks>
public sealed class PagePoller : IDisposable
{
	private readonly TimeProvider _time;
	private readonly Func<Func<Task>, Task> _dispatch;
	private readonly Func<bool> _mayTick;
	private CancellationTokenSource? _polling;
	private bool _disposed;

	/// <param name="dispatch">Runs a tick on the page's renderer: the component's <c>InvokeAsync</c>.</param>
	/// <param name="mayTick">Asked before every tick.</param>
	public PagePoller(TimeProvider time, Func<Func<Task>, Task> dispatch, Func<bool> mayTick)
	{
		ArgumentNullException.ThrowIfNull(time);
		ArgumentNullException.ThrowIfNull(dispatch);
		ArgumentNullException.ThrowIfNull(mayTick);
		_time = time;
		_dispatch = dispatch;
		_mayTick = mayTick;
	}

	public bool IsRunning => Volatile.Read(ref _polling) is not null;

	/// <summary>Calls <paramref name="tick"/> every <paramref name="interval"/> until stopped, disposed or <paramref name="lifetime"/> ends.</summary>
	public void Start(TimeSpan interval, Func<Task> tick, CancellationToken lifetime)
	{
		ArgumentNullException.ThrowIfNull(tick);
		ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
		Stop();
		if (_disposed || lifetime.IsCancellationRequested)
		{
			return;
		}

		CancellationTokenSource polling = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
		_polling = polling;
		_ = PollAsync(interval, tick, polling.Token);
	}

	public void Stop()
	{
		if (Interlocked.Exchange(ref _polling, null) is { } polling)
		{
			polling.Cancel();
			polling.Dispose();
		}
	}

	public void Dispose()
	{
		_disposed = true;
		Stop();
	}

	private async Task PollAsync(TimeSpan interval, Func<Task> tick, CancellationToken cancellationToken)
	{
		try
		{
			using PeriodicTimer timer = new(interval, _time);
			while (await timer.WaitForNextTickAsync(cancellationToken))
			{
				if (_mayTick())
				{
					await _dispatch(tick);
				}
			}
		}
		catch (OperationCanceledException)
		{
			// The page went away or polling was replaced.
		}
		catch (ObjectDisposedException)
		{
			// The renderer went away with the circuit.
		}
	}
}
