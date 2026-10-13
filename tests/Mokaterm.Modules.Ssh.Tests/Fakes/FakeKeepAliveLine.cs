using Mokaterm.Modules.Ssh.KeepAlive;

namespace Mokaterm.Modules.Ssh.Tests.Fakes;

/// <summary>
/// A connection as the keepalive loop sees it: the test decides when the server answers, what else it owes and whether
/// a keepalive may go out at all.
/// </summary>
internal sealed class FakeKeepAliveLine : IKeepAliveLine
{
	private readonly Lock _lock = new();
	private long _replies;
	private int _owed;
	private bool _closed;
	private bool _ignored;
	private int _stoppedAsking;
	private int _attempts;
	private int _sent;
	private KeepAliveSend? _refusal;

	/// <summary>What <see cref="TrySend"/> answers instead of sending, such as Waiting while a forward runs; null sends.</summary>
	public KeepAliveSend? Refusal
	{
		get
		{
			lock (_lock)
			{
				return _refusal;
			}
		}

		set
		{
			lock (_lock)
			{
				_refusal = value;
			}
		}
	}

	/// <summary>Every call to <see cref="TrySend"/>, which the loop makes once per interval while it runs.</summary>
	public int Attempts
	{
		get
		{
			lock (_lock)
			{
				return _attempts;
			}
		}
	}

	public int Sent
	{
		get
		{
			lock (_lock)
			{
				return _sent;
			}
		}
	}

	/// <summary>How many times the loop told the line to stop asking.</summary>
	public int StoppedAsking
	{
		get
		{
			lock (_lock)
			{
				return _stoppedAsking;
			}
		}
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

	public KeepAliveSend TrySend()
	{
		lock (_lock)
		{
			_attempts++;
			if (_closed)
			{
				return KeepAliveSend.Closed;
			}

			if (_ignored)
			{
				return KeepAliveSend.Waiting;
			}

			if (_refusal is { } refusal)
			{
				return refusal;
			}

			_owed++;
			_sent++;
			return KeepAliveSend.Sent;
		}
	}

	/// <summary>
	/// The server answers the oldest request it owes, or any request at all when it owes none. A line that stopped asking
	/// starts counting again, as the real one does.
	/// </summary>
	public void Reply()
	{
		lock (_lock)
		{
			_replies++;
			_owed = _ignored ? 0 : Math.Max(0, _owed - 1);
			_ignored = false;
		}
	}

	public void StopAsking()
	{
		lock (_lock)
		{
			_stoppedAsking++;
			_ignored = true;
			_owed = 0;
		}
	}

	/// <summary>A request that is not a keepalive, such as a forward start, now waits for its answer.</summary>
	public void Owe()
	{
		lock (_lock)
		{
			_owed++;
		}
	}

	public void Close()
	{
		lock (_lock)
		{
			_closed = true;
		}
	}
}
