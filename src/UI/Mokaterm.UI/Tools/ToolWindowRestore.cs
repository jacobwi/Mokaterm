using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Platform;
using Mokaterm.UI.Shell;
using Mokaterm.UI.Workspace;

namespace Mokaterm.UI.Tools;

/// <summary>
/// Remembers which tool windows are open, so the next start brings them back: an app tool's window once the vault is
/// unlocked, a session tool's once the session restore has reopened its login and the session has connected. A login
/// the restore does not reopen takes its tool windows with it. An entry leaves only when the user closes the window
/// or the tab of its session; the app closing a window (Mokaterm exiting, the vault locking, a session going away
/// with its window) keeps it, which is also what brings a window the lock closed back with the next unlock. With
/// <see cref="AppearanceSettings.ReopenToolWindows"/> off nothing comes back, and what was remembered is dropped then,
/// so the list always says what is open.
/// </summary>
internal sealed class ToolWindowRestore : IDisposable
{
	private readonly ShellToolWindows _windows;
	private readonly SessionToolActions _sessionTools;
	private readonly AppToolActions _appTools;
	private readonly SessionRestore _sessionRestore;
	private readonly ISessionManager _sessions;
	private readonly UiStateStore _uiState;
	private readonly ISettingsService _settings;
	private readonly Lock _gate = new();
	private readonly List<Entry> _entries = [];
	private readonly Dictionary<Guid, ISessionHandle> _watched = [];
	private IReadOnlyList<ToolWindowMemo> _persisted = [];
	private bool _started;
	private bool _disposed;

	public ToolWindowRestore(
		ShellToolWindows windows,
		SessionToolActions sessionTools,
		AppToolActions appTools,
		SessionRestore sessionRestore,
		ISessionManager sessions,
		UiStateStore uiState,
		ISettingsService settings)
	{
		_windows = windows;
		_sessionTools = sessionTools;
		_appTools = appTools;
		_sessionRestore = sessionRestore;
		_sessions = sessions;
		_uiState = uiState;
		_settings = settings;
	}

	/// <summary>
	/// Runs whenever the shell mounts, which is each unlock. The first call reads what the last run left and starts
	/// following the windows; every call opens the remembered windows that can open now. Its subscriptions are in place
	/// before it first waits, so call it before <see cref="SessionRestore.StartAsync"/>, whose sessions it watches for.
	/// </summary>
	public Task StartAsync()
	{
		if (!_windows.CanOpenWindows)
		{
			return Task.CompletedTask;
		}

		bool first;
		lock (_gate)
		{
			if (_disposed)
			{
				return Task.CompletedTask;
			}

			first = !_started;
			_started = true;
			if (first)
			{
				_persisted = _uiState.Current.RestoreToolWindows;

				// Session tools wait for their logins: the session restore says which come back as which session.
				_entries.AddRange(_persisted.Select(memo => new Entry(memo.Tool, memo.ConnectionId, null)));
			}
		}

		if (first)
		{
			_windows.Opened += OnOpened;
			_windows.ClosedByUser += OnClosedByUser;
			_sessionRestore.Reopened += OnReopened;
			_sessionRestore.Settled += OnSettled;
			_sessionRestore.Forgotten += OnForgotten;
			_sessions.SessionsChanged += OnSessionsChanged;
		}

		return ReopenAsync();
	}

	public void Dispose()
	{
		ISessionHandle[] watched;
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			watched = [.. _watched.Values];
			_watched.Clear();
		}

		foreach (ISessionHandle session in watched)
		{
			session.Changed -= OnWatchedChanged;
		}

		if (_started)
		{
			_windows.Opened -= OnOpened;
			_windows.ClosedByUser -= OnClosedByUser;
			_sessionRestore.Reopened -= OnReopened;
			_sessionRestore.Settled -= OnSettled;
			_sessionRestore.Forgotten -= OnForgotten;
			_sessions.SessionsChanged -= OnSessionsChanged;
		}
	}

	private static bool IsFor(Entry entry, ToolWindowTarget target) =>
		string.Equals(entry.Tool, target.ToolKey, StringComparison.Ordinal)
		&& (target.SessionId is { } sessionId ? entry.SessionId == sessionId : entry.ConnectionId is null);

	/// <summary>Opens the app tools' windows, and the session tools' whose sessions are open; the rest are watched for.</summary>
	private async Task ReopenAsync()
	{
		List<AppToolDescriptor> appTools = [];
		List<Guid> sessions = [];
		bool gone = false;
		bool reopen = _settings.Get<AppearanceSettings>().ReopenToolWindows;
		lock (_gate)
		{
			// Turned off: none of these windows will be open, so none of them is remembered either.
			if (!reopen)
			{
				gone = _entries.Count > 0;
				_entries.Clear();
			}

			foreach (Entry entry in _entries.ToList())
			{
				if (entry.ConnectionId is null)
				{
					// A plugin removed since: its window cannot come back, now or later.
					if (_appTools.Find(entry.Tool) is { } tool)
					{
						appTools.Add(tool);
					}
					else
					{
						gone |= _entries.Remove(entry);
					}
				}
				else if (entry.SessionId is { } sessionId && !sessions.Contains(sessionId))
				{
					sessions.Add(sessionId);
				}
			}
		}

		if (gone)
		{
			Persist();
		}

		bool opened = false;
		foreach (AppToolDescriptor tool in appTools.Where(tool => !_appTools.IsInWindow(tool)))
		{
			opened |= await _appTools.OpenWindowAsync(tool);
		}

		foreach (Guid sessionId in sessions)
		{
			if (_sessions.Find(sessionId) is { } session)
			{
				opened |= await OpenOrWatchAsync(session);
			}
		}

		ShowOwnerIf(opened);
	}

	/// <summary>Opens the session's remembered windows once it is connected. True when one opened now.</summary>
	private async Task<bool> OpenOrWatchAsync(ISessionHandle session)
	{
		if (session.State != SessionState.Connected)
		{
			Watch(session);
			return false;
		}

		string[] keys;
		lock (_gate)
		{
			keys = [.. _entries.Where(entry => entry.SessionId == session.Id).Select(entry => entry.Tool).Distinct(StringComparer.Ordinal)];
		}

		bool opened = false;
		foreach (string key in keys)
		{
			// A tool its protocol no longer has (a plugin removed) simply stays shut; the entry goes with the session.
			if (_sessionTools.Find(session, key) is { } tool && !_sessionTools.IsInWindow(session, tool))
			{
				opened |= await _sessionTools.OpenWindowAsync(session, tool);
			}
		}

		return opened;
	}

	private void Watch(ISessionHandle session)
	{
		lock (_gate)
		{
			if (_disposed || !_watched.TryAdd(session.Id, session))
			{
				return;
			}
		}

		session.Changed += OnWatchedChanged;

		// It may have connected between the check and the subscription.
		OnWatchedChanged();
	}

	private bool Unwatch(Guid sessionId)
	{
		ISessionHandle? session;
		lock (_gate)
		{
			if (!_watched.Remove(sessionId, out session))
			{
				return false;
			}
		}

		session.Changed -= OnWatchedChanged;
		return true;
	}

	// Started from an event, which nothing awaits; the windows open in their own time.
	private async Task OpenLaterAsync(ISessionHandle session) => ShowOwnerIf(await OpenOrWatchAsync(session));

	private void ShowOwnerIf(bool opened)
	{
		// The windows come back as they were, and the main window keeps the keyboard: the user was just typing there.
		if (opened)
		{
			_windows.ShowOwner();
		}
	}

	private void Persist()
	{
		List<ToolWindowMemo> memos;
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			memos = [.. _entries.Select(entry => new ToolWindowMemo(entry.Tool, entry.ConnectionId))];
			if (memos.SequenceEqual(_persisted))
			{
				return;
			}

			_persisted = memos;
		}

		_uiState.Update(state => state with { RestoreToolWindows = memos });
	}

	private void OnOpened(ToolWindowTarget target)
	{
		Entry entry;
		if (target.SessionId is { } sessionId)
		{
			// A quick-connect session has no saved login to come back as.
			if (_sessions.Find(sessionId) is not { IsTransient: false } session)
			{
				return;
			}

			entry = new Entry(target.ToolKey, session.Connection.Id, sessionId);
		}
		else
		{
			entry = new Entry(target.ToolKey, null, null);
		}

		lock (_gate)
		{
			if (_disposed || _entries.Exists(existing => IsFor(existing, target)))
			{
				return;
			}

			_entries.Add(entry);
		}

		Persist();
	}

	private void OnClosedByUser(ToolWindowTarget target)
	{
		lock (_gate)
		{
			if (_entries.RemoveAll(entry => IsFor(entry, target)) == 0)
			{
				return;
			}
		}

		Persist();
	}

	/// <summary>A login of the saved set is open again: its tools' windows belong to this session now.</summary>
	private void OnReopened(ISessionHandle session)
	{
		bool bound = false;
		lock (_gate)
		{
			HashSet<string> taken = [.. _entries.Where(entry => entry.SessionId == session.Id).Select(entry => entry.Tool)];
			for (int i = 0; i < _entries.Count; i++)
			{
				Entry entry = _entries[i];

				// One window per tool and session: a login that had two tabs gives each the windows it had.
				if (entry.SessionId is null && entry.ConnectionId == session.Connection.Id && taken.Add(entry.Tool))
				{
					_entries[i] = entry with { SessionId = session.Id };
					bound = true;
				}
			}
		}

		if (bound)
		{
			_ = OpenLaterAsync(session);
		}
	}

	/// <summary>The session restore is done: a session tool still waiting for its login waits for nothing.</summary>
	private void OnSettled()
	{
		lock (_gate)
		{
			if (_entries.RemoveAll(entry => entry.ConnectionId is not null && entry.SessionId is null) == 0)
			{
				return;
			}
		}

		Persist();
	}

	/// <summary>The user closed the session's tab, so its tools' windows do not come back either.</summary>
	private void OnForgotten(Guid sessionId)
	{
		_ = Unwatch(sessionId);
		lock (_gate)
		{
			if (_entries.RemoveAll(entry => entry.SessionId == sessionId) == 0)
			{
				return;
			}
		}

		Persist();
	}

	// A watched session that went away without the user closing it (Mokaterm closing) keeps its entries.
	private void OnSessionsChanged()
	{
		Guid[] gone;
		lock (_gate)
		{
			gone = [.. _watched.Keys.Where(id => _sessions.Find(id) is null)];
		}

		foreach (Guid id in gone)
		{
			_ = Unwatch(id);
		}
	}

	private void OnWatchedChanged()
	{
		ISessionHandle[] connected;
		lock (_gate)
		{
			connected = [.. _watched.Values.Where(session => session.State == SessionState.Connected)];
		}

		// Whoever takes it off the watch list opens its windows, so two changes at once cannot open them twice.
		foreach (ISessionHandle session in connected.Where(session => Unwatch(session.Id)))
		{
			_ = OpenLaterAsync(session);
		}
	}

	/// <summary>
	/// One remembered window: an app tool (no login), a session tool still waiting for its login to be reopened (no
	/// session yet), or a session tool on a session of this run.
	/// </summary>
	private sealed record Entry(string Tool, Guid? ConnectionId, Guid? SessionId);
}
