namespace Mokaterm.Modules.Ssh.KeepAlive;

/// <summary>
/// What OpenSSH's <c>ServerAliveInterval</c> and <c>ServerAliveCountMax</c> do, for one connection: every interval a
/// <c>keepalive@openssh.com</c> that wants a reply, and once the server owed a reply through
/// <see cref="KeepAlivePolicy.MaxMissed"/> intervals in a row and sent none, the connection counts as lost. SSH.NET's own
/// keepalive is a message that wants no answer, so a connection whose other end is gone shows only when TCP gives up,
/// which can take many minutes.
/// </summary>
/// <remarks>
/// <para>
/// Any reply resets the count, success or failure (OpenSSH answers a request it does not know with a failure), and so
/// does the answer to SSH.NET's own forward requests: either way the server is talking. An interval in which something of
/// ours held SSH.NET's message loop counts as neither, since no reply could have been read then. The first keepalive goes
/// out one interval after the start, so a tunnel opened with the session never waits for one, and a dead connection is
/// noticed after <c>MaxMissed + 1</c> intervals, as OpenSSH does.
/// </para>
/// <para>
/// Unlike OpenSSH's, which is off unless asked for, these keepalives run on every connection, so only a connection whose
/// server has answered at least one global request is judged. One that leaves the first <c>MaxMissed</c> intervals
/// unanswered belongs to a server or device that ignores requests it does not know: the line stops asking it
/// (<see cref="IKeepAliveLine.StopAsking"/>), it is never dropped, and SSH.NET's own keepalive goes on keeping NAT and
/// firewall state alive. Should it answer anything later, it is judged from then on.
/// </para>
/// </remarks>
internal sealed class KeepAliveMonitor : IAsyncDisposable
{
	private readonly IKeepAliveLine _line;
	private readonly KeepAlivePolicy _policy;
	private readonly TimeProvider _time;
	private readonly IReceiveHold? _hold;
	private readonly TaskCompletionSource<KeepAliveTimeoutException> _lost = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly CancellationTokenSource _stop = new();
	private readonly Task _loop;
	private int _missed;
	private long _intervals;
	private int _disposed;

	/// <param name="hold">What may hold up the message loop on this connection, if anything does.</param>
	public KeepAliveMonitor(IKeepAliveLine line, KeepAlivePolicy policy, TimeProvider time, IReceiveHold? hold = null)
	{
		ArgumentNullException.ThrowIfNull(line);
		ArgumentNullException.ThrowIfNull(policy);
		ArgumentNullException.ThrowIfNull(time);
		_line = line;
		_policy = policy;
		_time = time;
		_hold = hold;
		_loop = policy.IsOn ? RunAsync(_stop.Token) : Task.CompletedTask;
	}

	/// <summary>Completes once the server stopped answering. Never faults, and never completes while keepalives are off.</summary>
	public Task<KeepAliveTimeoutException> Lost => _lost.Task;

	/// <summary>
	/// Intervals in a row, so far, in which the server owed a reply and sent none. Before the server answered anything
	/// these lead to the line stopping asking rather than to <see cref="Lost"/>.
	/// </summary>
	public int MissedReplies => Volatile.Read(ref _missed);

	/// <summary>Intervals looked at so far that did not end the loop.</summary>
	public long Intervals => Interlocked.Read(ref _intervals);

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		await _stop.CancelAsync();
		await _loop;
		_stop.Dispose();
	}

	private async Task RunAsync(CancellationToken cancellationToken)
	{
		using PeriodicTimer timer = new(_policy.Interval, _time);
		long lastReplies = _line.State.Replies;
		long lastHolds = _hold?.Holds ?? 0;
		bool heldAtLastLook = _hold?.IsHeld ?? false;
		bool owed = false;
		try
		{
			while (await timer.WaitForNextTickAsync(cancellationToken))
			{
				KeepAliveLineState state = _line.State;
				if (state.Closed)
				{
					// A connection that is gone reaches the session through SSH.NET itself, with its own reason.
					return;
				}

				// A hold that started, ended or was under way at either end of the interval may have kept the reply unread.
				long holds = _hold?.Holds ?? 0;
				bool heldNow = _hold?.IsHeld ?? false;
				bool held = heldNow || heldAtLastLook || holds != lastHolds;
				lastHolds = holds;
				heldAtLastLook = heldNow;

				// A line that stopped asking owes nothing, which is also what a forward start that found no answer leaves.
				if (state.Replies != lastReplies || state.Ignored)
				{
					Volatile.Write(ref _missed, 0);
				}
				else if (owed && !held)
				{
					Volatile.Write(ref _missed, _missed + 1);
				}

				lastReplies = state.Replies;
				if (_missed >= _policy.MaxMissed)
				{
					// Only a server that has answered something can be judged by what it leaves unanswered.
					if (state.Replies > 0)
					{
						_lost.TrySetResult(KeepAliveTimeoutException.Missed(_missed));
						return;
					}

					_line.StopAsking();
					Volatile.Write(ref _missed, 0);
				}

				if (!_line.State.Ignored && _line.TrySend() == KeepAliveSend.Closed)
				{
					return;
				}

				// Whatever the server owes now, ours or a forward's, has the next interval to arrive.
				owed = _line.State.Owed > 0;
				_ = Interlocked.Increment(ref _intervals);
			}
		}
		catch (OperationCanceledException)
		{
			// Disposed.
		}
	}
}
