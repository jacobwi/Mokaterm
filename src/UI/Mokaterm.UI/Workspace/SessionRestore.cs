using System.Globalization;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Shell;

namespace Mokaterm.UI.Workspace;

/// <summary>
/// Remembers which saved logins have a tab, which pane each tab is in and how the panes are laid out, so the next start
/// can offer them again. An entry leaves the set only when the user closes that tab: a window or a circuit going away
/// takes its sessions with it, and that must not read as "the user closed everything".
/// </summary>
internal sealed class SessionRestore : IDisposable
{
	private readonly ISessionManager _manager;
	private readonly SessionWorkspace _workspace;
	private readonly UiStateStore _uiState;
	private readonly ISettingsService _settings;
	private readonly IUserInteraction _interaction;
	private readonly ILogger<SessionRestore> _logger;
	private readonly Lock _gate = new();
	private readonly List<RestoreEntry> _entries = [];
	private SavedTabs _pending = SavedTabs.Empty;
	private SavedTabs _persisted = SavedTabs.Empty;
	private PaneNode? _layout;
	private bool _started;
	private bool _settled;
	private bool _disposed;

	public SessionRestore(
		ISessionManager manager,
		SessionWorkspace workspace,
		UiStateStore uiState,
		ISettingsService settings,
		IUserInteraction interaction,
		ILogger<SessionRestore> logger)
	{
		_manager = manager;
		_workspace = workspace;
		_uiState = uiState;
		_settings = settings;
		_interaction = interaction;
		_logger = logger;
	}

	/// <summary>Raised when the offer appears or goes away. Any thread.</summary>
	public event Action? Changed;

	/// <summary>A login from the saved set is open again, as this session. Raised while restoring, on its thread.</summary>
	public event Action<ISessionHandle>? Reopened;

	/// <summary>
	/// The saved set has been dealt with: reopened, turned down, gone stale because the user opened something else, or
	/// there was nothing to offer. Raised once; a login of the saved set that was not reopened by then never will be.
	/// </summary>
	public event Action? Settled;

	/// <summary>The user closed this session's tab, so nothing of it comes back at the next start. Any thread.</summary>
	public event Action<Guid>? Forgotten;

	/// <summary>Logins saved by the previous run that have not been reopened or turned down yet.</summary>
	public IReadOnlyList<Guid> Pending
	{
		get
		{
			lock (_gate)
			{
				return _pending.ConnectionIds;
			}
		}
	}

	public SessionRestoreMode Mode => _settings.Get<GeneralSettings>().RestoreSessions;

	/// <summary>Reads the saved set and starts following the open tabs. Only the first call does anything.</summary>
	public async Task StartAsync()
	{
		lock (_gate)
		{
			if (_started || _disposed)
			{
				return;
			}

			_started = true;
			_persisted = SavedTabs.From(_uiState.Current);
			if (Mode != SessionRestoreMode.Off)
			{
				_pending = _persisted;
			}
		}

		_workspace.Changed += OnWorkspaceChanged;
		OnWorkspaceChanged();

		if (Mode == SessionRestoreMode.Always)
		{
			await RestoreAsync();
		}
		else if (Pending.Count > 0)
		{
			Changed?.Invoke();
		}

		// Always settled by the restore itself; an offer settles when it is answered or goes stale.
		if (Pending.Count == 0)
		{
			Settle();
		}
	}

	/// <summary>Drops a session the user closed, so the next start does not bring it back.</summary>
	public void Forget(Guid sessionId)
	{
		bool removed;
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			removed = _entries.RemoveAll(entry => entry.SessionId == sessionId) > 0;
		}

		Forgotten?.Invoke(sessionId);
		if (removed)
		{
			Persist();
		}
	}

	/// <summary>Opens every login in the saved set, in the order their tabs had, each in the pane it was in.</summary>
	public async Task RestoreAsync()
	{
		SavedTabs saved = TakePending();
		if (saved.ConnectionIds.Count == 0)
		{
			return;
		}

		_workspace.BeginRestore(saved.Layout, [.. saved.ConnectionIds.Select((id, index) => new PanePlacement(id, saved.PaneAt(index)))]);
		int failed = 0;
		try
		{
			foreach (Guid connectionId in saved.ConnectionIds)
			{
				try
				{
					ISessionHandle session = await _manager.OpenAsync(connectionId);
					Reopened?.Invoke(session);
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					// A login deleted since the last run is the usual reason, and one bad id must not stop the rest.
					failed++;
					_logger.LogWarning(ex, "Reopening connection {ConnectionId} failed.", connectionId);
				}
			}
		}
		finally
		{
			_workspace.EndRestore();
			Settle();
		}

		if (failed > 0)
		{
			_interaction.Notify(
				NoticeSeverity.Warning,
				failed == 1
					? "One session could not be reopened. Its login may be gone."
					: string.Create(CultureInfo.CurrentCulture, $"{failed} sessions could not be reopened. Their logins may be gone."));
		}
	}

	/// <summary>Turns the offer down. What is open from now on takes the saved set's place.</summary>
	public void Dismiss()
	{
		if (TakePending().ConnectionIds.Count == 0)
		{
			return;
		}

		Persist();
		Settle();
	}

	public void Dispose()
	{
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
		}

		_workspace.Changed -= OnWorkspaceChanged;
	}

	private static bool Covers(PaneNode layout, List<RestoreEntry> entries) =>
		entries.TrueForAll(entry => PaneLayout.Contains(layout, entry.PaneId));

	private void Settle()
	{
		lock (_gate)
		{
			if (_settled || _disposed)
			{
				return;
			}

			_settled = true;
		}

		Settled?.Invoke();
	}

	private SavedTabs TakePending()
	{
		SavedTabs saved;
		lock (_gate)
		{
			saved = _pending;
			_pending = SavedTabs.Empty;
		}

		if (saved.ConnectionIds.Count > 0)
		{
			Changed?.Invoke();
		}

		return saved;
	}

	private void OnWorkspaceChanged()
	{
		WorkspaceSnapshot snapshot = _workspace.Snapshot();
		bool offerGone = false;
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			Queue<RestoreEntry> tabs = new();
			HashSet<Guid> open = [];
			foreach (PanedTab tab in snapshot.Tabs)
			{
				// A quick-connect tab has no saved login to reopen.
				if (tab.Session.IsTransient || !open.Add(tab.Session.Id))
				{
					continue;
				}

				tabs.Enqueue(new RestoreEntry(tab.Session.Id, tab.Session.Connection.Id, tab.PaneId));
			}

			// Tabs that went away without a close stay, and in their place: that is a window closing, not a user, and it
			// closes its sessions in no particular order. The open tabs fill the other places in tab order, and tabs that
			// are new since the last change go last.
			List<RestoreEntry> next = [];
			foreach (RestoreEntry entry in _entries)
			{
				if (!open.Contains(entry.SessionId))
				{
					next.Add(entry);
				}
				else if (tabs.TryDequeue(out RestoreEntry? tab))
				{
					next.Add(tab);
				}
			}

			next.AddRange(tabs);
			_entries.Clear();
			_entries.AddRange(next);

			// A window closing takes its tabs one at a time and collapses their panes as it goes, so the layout saved is the
			// last one that still held every remembered tab's pane.
			if (_layout is null || Covers(snapshot.Layout, _entries) || !Covers(_layout, _entries))
			{
				_layout = snapshot.Layout;
			}

			if (_pending.ConnectionIds.Count > 0 && open.Count > 0)
			{
				// The user got going on their own, so the offer is stale.
				_pending = SavedTabs.Empty;
				offerGone = true;
			}
		}

		if (offerGone)
		{
			Changed?.Invoke();
			Settle();
		}

		Persist();
	}

	private void Persist()
	{
		SavedTabs saved;
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			saved = _pending.ConnectionIds.Count > 0 ? _pending.Append(_entries) : Current();
			if (saved.SameAs(_persisted))
			{
				return;
			}

			_persisted = saved;
		}

		_uiState.Update(state => state with
		{
			RestoreConnectionIds = saved.ConnectionIds,
			RestorePanes = saved.Panes,
			RestoreLayout = saved.Layout,
		});
	}

	/// <summary>The remembered tabs against the layout, cut down to the panes they are in.</summary>
	private SavedTabs Current()
	{
		HashSet<int> used = [.. _entries.Select(entry => entry.PaneId)];
		PaneNode? layout = _layout is null ? null : PaneLayout.Prune(_layout, used.Contains);
		if (layout is null)
		{
			return new SavedTabs([.. _entries.Select(entry => entry.ConnectionId)], [], null);
		}

		IReadOnlyList<int> panes = PaneLayout.Panes(layout);
		List<int> positions = [.. _entries.Select(entry => Math.Max(0, PaneLayout.IndexOf(panes, entry.PaneId)))];

		// One pane is what a restore does without being told, so the file only says more when there is more to say.
		return panes.Count > 1
			? new SavedTabs([.. _entries.Select(entry => entry.ConnectionId)], positions, PaneLayout.Write(layout))
			: new SavedTabs([.. _entries.Select(entry => entry.ConnectionId)], [], null);
	}

	private sealed record RestoreEntry(Guid SessionId, Guid ConnectionId, int PaneId);

	/// <summary>The logins to reopen in tab order, the pane each goes to, and the layout those panes are counted in.</summary>
	private sealed record SavedTabs(IReadOnlyList<Guid> ConnectionIds, IReadOnlyList<int> Panes, string? Layout)
	{
		public static SavedTabs Empty { get; } = new([], [], null);

		public static SavedTabs From(UiStateDocument document) =>
			new([.. document.RestoreConnectionIds], [.. document.RestorePanes], document.RestoreLayout);

		/// <summary>The pane for the login at <paramref name="index"/>; one saved without a pane goes to the first.</summary>
		public int PaneAt(int index) => index < Panes.Count ? Panes[index] : 0;

		/// <summary>The saved set with tabs opened since added after it, in the first pane.</summary>
		public SavedTabs Append(IReadOnlyList<RestoreEntry> entries) => entries.Count == 0
			? this
			: new SavedTabs(
				[.. ConnectionIds, .. entries.Select(entry => entry.ConnectionId)],
				[.. ConnectionIds.Select((_, index) => PaneAt(index)), .. entries.Select(_ => 0)],
				Layout);

		public bool SameAs(SavedTabs other) =>
			ConnectionIds.SequenceEqual(other.ConnectionIds) && Panes.SequenceEqual(other.Panes) && Layout == other.Layout;
	}
}
