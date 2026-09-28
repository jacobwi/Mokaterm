using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Sessions.Terminal;

namespace Mokaterm.Sessions;

/// <summary>
/// The state of one session tab across reconnects. Connect attempts are numbered: an update from an attempt that
/// is no longer current, or that arrives after the tab closed, is ignored instead of overwriting newer state.
/// </summary>
internal sealed class SessionHandle : ISessionHandle, IDisposable
{
	private readonly Lock _lock = new();
	private readonly CancellationTokenSource _lifetime = new();
	private readonly ILogger _logger;

	// Replaced for every connect attempt and cancelled by a disconnect or close. Not linked to the lifetime source,
	// so a replaced instance holds no registration and needs no disposal while a late provider may still observe it.
	private CancellationTokenSource _activity = new();

	// Kept beside the source: reading Token from a disposed source throws, and a reconnect loop may ask after closing.
	private CancellationToken _activityToken;
	private readonly string? _titleOverride;
	private readonly string? _titleSuffix;
	private string _title;
	private HostProfile _host;
	private ConnectionProfile _connection;
	private SessionState _state;
	private string? _statusMessage;
	private ConnectFailure? _failure;
	private DateTimeOffset? _connectedAt;
	private IProtocolSession? _session;
	private IProtocolSession? _endedSession;
	private int _attempt;
	private bool _closed;

	public SessionHandle(
		HostProfile host,
		ConnectionProfile connection,
		ProtocolDescriptor protocol,
		bool isTransient,
		DateTimeOffset openedAt,
		TerminalStream? terminal,
		string? titleOverride,
		string? titleSuffix,
		ILogger logger)
	{
		Id = Guid.NewGuid();
		Protocol = protocol;
		IsTransient = isTransient;
		OpenedAt = openedAt;
		TerminalStream = terminal;
		Lifetime = _lifetime.Token;
		_activityToken = _activity.Token;
		_titleOverride = titleOverride;
		_titleSuffix = titleSuffix;
		_logger = logger;
		_host = host;
		_connection = connection;
		_title = BuildTitle(host, connection);
	}

	public event Action? Changed;

	public Guid Id { get; }

	public ProtocolDescriptor Protocol { get; }

	public bool IsTransient { get; private set; }

	public DateTimeOffset OpenedAt { get; }

	public ITerminalStream? Terminal => TerminalStream;

	public string Title => Read(() => _title);

	public HostProfile Host => Read(() => _host);

	public ConnectionProfile Connection => Read(() => _connection);

	public SessionState State => Read(() => _state);

	public string? StatusMessage => Read(() => _statusMessage);

	public ConnectFailure? Failure => Read(() => _failure);

	public DateTimeOffset? ConnectedAt => Read(() => _connectedAt);

	public IProtocolSession? Session => Read(() => _session);

	internal TerminalStream? TerminalStream { get; }

	/// <summary>Cancelled when the tab closes. Stays usable after the handle is disposed.</summary>
	internal CancellationToken Lifetime { get; }

	/// <summary>
	/// Cancelled when the current activity stops: a disconnect or close. Covers the connect attempt in progress and a
	/// pending automatic reconnect.
	/// </summary>
	internal CancellationToken ActivityToken => Read(() => _activityToken);

	/// <summary>
	/// Starts a connect attempt for a new, disconnected or failed session and returns its number, or 0 when the
	/// session is closed or already connecting or connected. <paramref name="endedSession"/> is the session left
	/// over from the previous connection, which the caller disposes. <paramref name="cancellationToken"/> is
	/// cancelled when the user disconnects or closes during the attempt.
	/// </summary>
	/// <param name="afterAttempt">
	/// For an automatic reconnect: the attempt it follows. When any other attempt started since, the user took over and
	/// nothing starts.
	/// </param>
	internal int TryBeginAttempt(string statusMessage, int? afterAttempt, out IProtocolSession? endedSession, out CancellationToken cancellationToken)
	{
		endedSession = null;
		cancellationToken = CancellationToken.None;
		int attempt;
		lock (_lock)
		{
			bool canStart = _attempt == 0 || ISessionHandle.CanReconnect(_state);
			if (_closed || !canStart || (afterAttempt is { } previous && previous != _attempt))
			{
				return 0;
			}

			attempt = ++_attempt;
			if (_activity.IsCancellationRequested)
			{
				_activity = new CancellationTokenSource();
				_activityToken = _activity.Token;
			}

			cancellationToken = _activityToken;
			endedSession = _endedSession;
			_endedSession = null;
			_state = SessionState.Connecting;
			_statusMessage = statusMessage;
			_failure = null;
			_connectedAt = null;
			_session = null;
		}

		RaiseChanged();
		return attempt;
	}

	internal void ReportStatus(int attempt, string statusMessage)
	{
		lock (_lock)
		{
			if (!IsCurrent(attempt) || _state != SessionState.Connecting || _statusMessage == statusMessage)
			{
				return;
			}

			_statusMessage = statusMessage;
		}

		RaiseChanged();
	}

	/// <summary>True while <paramref name="attempt"/> is the current attempt and still connecting.</summary>
	internal bool IsConnecting(int attempt) => Read(() => IsCurrent(attempt) && _state == SessionState.Connecting);

	internal bool TryMarkConnected(int attempt, IProtocolSession session, DateTimeOffset connectedAt)
	{
		lock (_lock)
		{
			if (!IsCurrent(attempt) || _state != SessionState.Connecting)
			{
				return false;
			}

			_state = SessionState.Connected;
			_session = session;
			_connectedAt = connectedAt;
			_statusMessage = null;
			_failure = null;
		}

		RaiseChanged();
		return true;
	}

	/// <summary>Moves a connecting or connected attempt to <paramref name="state"/>. False when the attempt is stale.</summary>
	internal bool TryMarkEnded(int attempt, SessionState state, string statusMessage, ConnectFailure? failure)
	{
		lock (_lock)
		{
			if (!IsCurrent(attempt) || _state is not (SessionState.Connecting or SessionState.Connected))
			{
				return false;
			}

			_state = state;
			_statusMessage = statusMessage;
			_failure = failure;
			_endedSession = _session;
			_session = null;
		}

		RaiseChanged();
		return true;
	}

	/// <summary>
	/// Replaces the status of a disconnected or failed session, for example with a reconnect countdown. False when the
	/// session is closed, not ended, or another attempt started after <paramref name="attempt"/>.
	/// </summary>
	internal bool TrySetEndedStatus(string statusMessage, int attempt)
	{
		lock (_lock)
		{
			if (_closed || attempt != _attempt || !ISessionHandle.CanReconnect(_state))
			{
				return false;
			}

			_statusMessage = statusMessage;
		}

		RaiseChanged();
		return true;
	}

	/// <summary>
	/// Stops the current activity without closing the tab. A connecting or connected session moves to Disconnected
	/// and hands its live protocol session to the caller for disposal; a disconnected or failed session only has its
	/// pending automatic reconnect cancelled. False when the tab is closed or there is nothing to stop.
	/// </summary>
	internal bool TryDisconnect(out IProtocolSession? session)
	{
		session = null;
		CancellationTokenSource activity;
		lock (_lock)
		{
			activity = _activity;
			if (_closed)
			{
				return false;
			}

			if (_state is SessionState.Connecting or SessionState.Connected)
			{
				// The attempt number stays: the cancelled attempt's late result finds the state already ended and is ignored.
				session = _session;
				_session = null;
				_state = SessionState.Disconnected;
				_statusMessage = "Disconnected";
				_failure = null;
			}
			else if (activity.IsCancellationRequested)
			{
				return false;
			}
		}

		activity.Cancel();
		RaiseChanged();
		return true;
	}

	/// <summary>
	/// Hands a quick-connect session over to the login it was just saved as, so the open tab is the saved one from
	/// here on: it reconnects from the login, its saved commands belong to it, and it can be edited.
	/// </summary>
	internal void Adopt(HostProfile host, ConnectionProfile connection)
	{
		lock (_lock)
		{
			_host = host;
			_connection = connection;
			_title = BuildTitle(host, connection);
			IsTransient = false;
		}

		RaiseChanged();
	}

	internal void UpdateProfiles(HostProfile host, ConnectionProfile connection)
	{
		lock (_lock)
		{
			_host = host;
			_connection = connection;
			_title = BuildTitle(host, connection);
		}

		RaiseChanged();
	}

	/// <summary>
	/// Marks the session closed and cancels <see cref="Lifetime"/>. Only the first call returns true; it also
	/// hands over the live or ended protocol session for the caller to dispose.
	/// </summary>
	internal bool TryClose(out IProtocolSession? session)
	{
		session = null;
		CancellationTokenSource activity;
		lock (_lock)
		{
			if (_closed)
			{
				return false;
			}

			_closed = true;
			activity = _activity;
			session = _session ?? _endedSession;
			_session = null;
			_endedSession = null;
			if (_state is SessionState.Connecting or SessionState.Connected)
			{
				_state = SessionState.Disconnected;
				_statusMessage = "Closed";
				_failure = null;
			}
		}

		// Outside the lock: cancellation runs the provider's callbacks, which may report status back here.
		activity.Cancel();
		_lifetime.Cancel();
		RaiseChanged();
		return true;
	}

	public void Dispose()
	{
		_lifetime.Dispose();
		Read(() => _activity).Dispose();
	}

	private bool IsCurrent(int attempt) => !_closed && attempt == _attempt;

	private string BuildTitle(HostProfile host, ConnectionProfile connection) =>
		_titleOverride ?? connection.GetTitle(host) + _titleSuffix;

	private T Read<T>(Func<T> read)
	{
		lock (_lock)
		{
			return read();
		}
	}

	private void RaiseChanged() => EventRaiser.Raise(Changed, _logger, nameof(Changed));
}
