using Mokaterm.Modules.Ssh.Forwarding;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Messages;
using Renci.SshNet.Messages.Connection;

namespace Mokaterm.Modules.Ssh.KeepAlive;

/// <summary>
/// The global requests that want a reply on one SSH.NET session, ours and SSH.NET's own, counted against the replies so
/// that every reply reaches the request it answers.
/// </summary>
/// <remarks>
/// <para>
/// A reply to a global request carries nothing that says which request it answers: the server answers in the order the
/// requests arrived. SSH.NET 2026.0.0's <see cref="ForwardedPortRemote"/> takes the first reply after it subscribes as
/// the answer to its <c>tcpip-forward</c>, and it stays subscribed while the port runs, so every later reply, whoever
/// asked, sets its wait handle. Its stop can then return before the answer to its <c>cancel-tcpip-forward</c> arrived,
/// which leaves that answer for whatever asks next. A keepalive still unanswered when a forward starts would hand the
/// forward its reply: OpenSSH answers <c>keepalive@openssh.com</c> with a failure, so the tunnel would fail while the
/// server listens on it, and the forward's own answer would then pass for the keepalive's.
/// </para>
/// <para>
/// So every request is counted before it goes out and every reply takes one off. A forward start waits until nothing is
/// owed, and no keepalive goes out while a forward start or stop runs; a stop counts its cancel, which keeps the next start
/// waiting for that answer too. In SSH.NET 2026.0.0 the remote forward is the only thing that sends a global request,
/// apart from stopping every port when a client disconnects, which happens after this line is closed, and the tunnel
/// manager is the only thing here that starts or stops one. Jump hosts never forward remote ports, and each hop is a
/// session with a reply order of its own.
/// </para>
/// <para>
/// Some servers and devices never answer a global request they do not know, and keepalives have been on for every
/// connection since long before they wanted a reply. Those keepalives would be owed forever and hold every forward start,
/// so once a server is found not to answer (<see cref="StopAsking"/>) the line asks nothing, owes nothing and lets forwards
/// run as SSH.NET runs them, until the server answers anything at all.
/// </para>
/// </remarks>
internal sealed class GlobalRequestLine : IKeepAliveLine, IForwardRequests, IDisposable
{
	/// <summary>
	/// How long a forward start waits on a connection whose server never answered anything. A server that answers does so
	/// within a round trip; one that has kept a keepalive unanswered this long is taken not to answer them at all.
	/// </summary>
	internal static readonly TimeSpan UnprovenWait = TimeSpan.FromSeconds(5);

	private const string SuccessMessageName = "SSH_MSG_REQUEST_SUCCESS";

	private const string FailureMessageName = "SSH_MSG_REQUEST_FAILURE";

	private readonly Session _session;
	private readonly ISshMessageSender _sender;
	private readonly Func<Message?> _createProbe;
	private readonly TimeSpan _settleLimit;
	private readonly TimeProvider _time;
	private readonly IReceiveHold? _hold;
	private readonly Lock _lock = new();
	private readonly TaskCompletionSource _stoppedAsking = new(TaskCreationOptions.RunContinuationsAsynchronously);

	// Forward starts and stops take turns: a cancel sent after a start stopped waiting for quiet would hand it that reply.
	// Never disposed: a stop still running when the line closes would throw from its release.
	private readonly SemaphoreSlim _forwardTurn = new(1, 1);
	private TaskCompletionSource _settled = NewSignal();
	private long _replies;
	private int _owed;
	private bool _forwarding;
	private bool _writing;
	private bool _ignored;
	private bool _closed;

	/// <param name="createProbe">Builds one keepalive that wants a reply, or returns null when none can be built.</param>
	/// <param name="settleLimit">
	/// How long a forward start waits for earlier requests to be answered before it gives up, once the server has answered
	/// something; until then it waits <see cref="UnprovenWait"/> at most.
	/// </param>
	/// <param name="hold">What may hold up this session's message loop, during which no reply can be read.</param>
	public GlobalRequestLine(Session session, ISshMessageSender sender, Func<Message?> createProbe, TimeSpan settleLimit, TimeProvider time, IReceiveHold? hold = null)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(sender);
		ArgumentNullException.ThrowIfNull(createProbe);
		ArgumentNullException.ThrowIfNull(time);
		_session = session;
		_sender = sender;
		_createProbe = createProbe;
		_settleLimit = settleLimit;
		_time = time;
		_hold = hold;
		session.RequestSuccessReceived += OnSuccess;
		session.RequestFailureReceived += OnFailure;
	}

	public KeepAliveLineState State
	{
		get
		{
			lock (_lock)
			{
				return new KeepAliveLineState(_replies, _owed, _closed, _ignored);
			}
		}
	}

	/// <summary>Completes the first time the line stops asking because the server answered nothing.</summary>
	public Task StoppedAsking => _stoppedAsking.Task;

	/// <summary>
	/// The line for <paramref name="client"/>'s session, which must have connected, or null with the reason when SSH.NET
	/// would not hand it over.
	/// </summary>
	public static GlobalRequestLine? Open(BaseClient client, TimeSpan settleLimit, TimeProvider time, IReceiveHold? hold, out string? problem)
	{
		ArgumentNullException.ThrowIfNull(client);
		Session? session = SshNetInternals.GetSession(client);
		ISshMessageSender? sender = session is null ? null : SshNetInternals.CreateSender(session);
		if (session is null || sender is null)
		{
			problem = SshNetInternals.SessionProblem ?? "SSH.NET did not hand over the session.";
			return null;
		}

		try
		{
			// SSH.NET accepts both replies once the login succeeded, and a reply it does not accept ends the connection, so
			// asking again keeps that true should a later version stop doing it. ForwardedPortRemote asks the same way. Its
			// message table only exists once the session connected, which every caller here already did.
			session.RegisterMessage(SuccessMessageName);
			session.RegisterMessage(FailureMessageName);
		}
		catch (Exception ex) when (ex is SshException or ArgumentException or InvalidOperationException)
		{
			problem = $"SSH.NET would not take replies to global requests ({ex.GetType().Name}).";
			return null;
		}

		problem = null;
		return new GlobalRequestLine(session, sender, SshNetInternals.CreateKeepAliveRequest, settleLimit, time, hold);
	}

	public KeepAliveSend TrySend()
	{
		lock (_lock)
		{
			if (_closed)
			{
				return KeepAliveSend.Closed;
			}

			if (_ignored || _forwarding || _writing)
			{
				return KeepAliveSend.Waiting;
			}

			_owed++;
			_writing = true;
		}

		// SSH.NET writes on the caller's thread, waits out a key exchange first, and a socket whose peer is gone can block
		// a write, so the loop that judges the connection never waits for one.
		_ = Task.Run(WriteProbe);
		return KeepAliveSend.Sent;
	}

	public void StopAsking()
	{
		lock (_lock)
		{
			if (_ignored || _closed)
			{
				return;
			}

			// Whatever was sent is taken as never to be answered. A reply that still comes is counted and ends this.
			_ignored = true;
			_owed = 0;
			SignalIfSettled();
		}

		_ = _stoppedAsking.TrySetResult();
	}

	public async Task StartAsync(Action start, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(start);
		await _forwardTurn.WaitAsync(cancellationToken);
		try
		{
			SetForwarding(true);
			try
			{
				bool counted = await SettleAsync(cancellationToken);
				lock (_lock)
				{
					if (_closed)
					{
						throw Closed();
					}

					// A server that does not answer is not asked to: the forward runs as SSH.NET runs it, uncounted.
					counted &= !_ignored;
					if (counted)
					{
						_owed++;
					}
				}

				try
				{
					await Task.Run(start, CancellationToken.None);
				}
				catch (Exception ex) when (counted && ex is InvalidOperationException or SshConnectionException)
				{
					// Refused before anything went out (the port was not ready or the connection is gone; an
					// ObjectDisposedException is an InvalidOperationException), so no reply is coming. Any other failure
					// came after the request was sent, and its reply is either counted already or still on its way.
					TakeBack();
					throw;
				}
			}
			finally
			{
				SetForwarding(false);
			}
		}
		finally
		{
			_forwardTurn.Release();
		}
	}

	public async Task StopAsync(Action stop, bool sendsRequest)
	{
		ArgumentNullException.ThrowIfNull(stop);

		// No cancellation: a skipped stop leaves the port listening on the server, and every wait in here is bounded by SSH.NET.
		await _forwardTurn.WaitAsync(CancellationToken.None);
		try
		{
			SetForwarding(true);
			bool counted = false;
			lock (_lock)
			{
				if (sendsRequest && !_closed && !_ignored)
				{
					_owed++;
					counted = true;
				}
			}

			try
			{
				await Task.Run(stop, CancellationToken.None);
			}
			catch (Exception ex) when (counted && ex is InvalidOperationException or SshConnectionException)
			{
				TakeBack();
				throw;
			}
			finally
			{
				SetForwarding(false);
			}
		}
		finally
		{
			_forwardTurn.Release();
		}
	}

	/// <summary>Stops counting and wakes a forward start that waits; stops still run, uncounted, since the connection is going.</summary>
	public void Dispose()
	{
		_session.RequestSuccessReceived -= OnSuccess;
		_session.RequestFailureReceived -= OnFailure;
		lock (_lock)
		{
			_closed = true;
			SignalIfSettled();
		}
	}

	private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

	private static SshConnectionException Closed() => new("The SSH connection is closed.");

	private static TimeoutException Unanswered() => new("The server has not answered an earlier request yet. Try again in a moment.");

	private void OnSuccess(object? sender, MessageEventArgs<RequestSuccessMessage> e) => CountReply();

	private void OnFailure(object? sender, MessageEventArgs<RequestFailureMessage> e) => CountReply();

	/// <summary>Runs on SSH.NET's message loop, where an exception would end the connection: a short lock and nothing more.</summary>
	private void CountReply()
	{
		lock (_lock)
		{
			_replies++;
			if (_ignored)
			{
				// The server does answer after all, a late keepalive or a forward. Counting starts over from here.
				_ignored = false;
			}
			else if (_owed > 0)
			{
				_owed--;
			}

			SignalIfSettled();
		}
	}

	private void WriteProbe()
	{
		bool sent;
		try
		{
			sent = _createProbe() is { } probe && _sender.TrySend(probe);
		}
		catch (Exception)
		{
			// Building the message runs SSH.NET's own constructor; whatever it throws means nothing went out.
			sent = false;
		}

		bool connected = sent || IsConnected();
		lock (_lock)
		{
			_writing = false;
			if (!sent)
			{
				_owed = Math.Max(0, _owed - 1);
				_closed |= !connected;
			}

			SignalIfSettled();
		}
	}

	private bool IsConnected()
	{
		try
		{
			return _session.IsConnected;
		}
		catch (ObjectDisposedException)
		{
			return false;
		}
	}

	private void TakeBack()
	{
		lock (_lock)
		{
			_owed = Math.Max(0, _owed - 1);
			SignalIfSettled();
		}
	}

	private void SetForwarding(bool forwarding)
	{
		lock (_lock)
		{
			_forwarding = forwarding;
		}
	}

	/// <summary>Called under the lock after every change that can leave nothing owed.</summary>
	private void SignalIfSettled()
	{
		if (_closed || _ignored || (_owed == 0 && !_writing))
		{
			_ = _settled.TrySetResult();
		}
	}

	/// <summary>
	/// Waits until nothing is owed, then returns true: the forward is counted. Returns false at once on a line that stopped
	/// asking, or after <see cref="UnprovenWait"/> on a connection whose server never answered anything, which then stops
	/// asking: the forward runs uncounted, as it did before keepalives wanted a reply.
	/// </summary>
	/// <exception cref="TimeoutException">A server that answered before kept an earlier request unanswered too long.</exception>
	private async Task<bool> SettleAsync(CancellationToken cancellationToken)
	{
		long started = _time.GetTimestamp();
		long holds = _hold?.Holds ?? 0;
		while (true)
		{
			Task signal;
			bool proven;
			lock (_lock)
			{
				if (_closed)
				{
					throw Closed();
				}

				if (_ignored)
				{
					return false;
				}

				if (_owed == 0 && !_writing)
				{
					return true;
				}

				if (_settled.Task.IsCompleted)
				{
					_settled = NewSignal();
				}

				signal = _settled.Task;
				proven = _replies > 0;
			}

			TimeSpan limit = proven || _settleLimit < UnprovenWait ? _settleLimit : UnprovenWait;
			TimeSpan remaining = limit - _time.GetElapsedTime(started);
			bool timedOut = remaining <= TimeSpan.Zero;
			if (!timedOut)
			{
				try
				{
					await signal.WaitAsync(remaining, _time, cancellationToken);
				}
				catch (TimeoutException)
				{
					timedOut = true;
				}
			}

			if (!timedOut)
			{
				continue;
			}

			// Going ahead while a reply may still come would let it pass for the forward's own. On a server that never
			// answered anything none is coming, unless our own flow control kept it unread, so the line stops asking and
			// the forward goes ahead. A server that has answered before is slow or gone, and the start gives up instead.
			if (proven || IsHeldSince(holds))
			{
				throw Unanswered();
			}

			StopAsking();
		}
	}

	private bool IsHeldSince(long holds) => _hold is { } hold && (hold.IsHeld || hold.Holds != holds);
}
