using System.Globalization;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Shell;

namespace Mokaterm.UI.Workspace;

/// <summary>
/// Remembers which saved logins have a tab, so the next start can offer them again. An entry leaves the set only when
/// the user closes that tab: a window or a circuit going away takes its sessions with it, and that must not read as
/// "the user closed everything".
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
	private List<Guid> _pending = [];
	private List<Guid> _persisted = [];
	private bool _started;
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

	/// <summary>Logins saved by the previous run that have not been reopened or turned down yet.</summary>
	public IReadOnlyList<Guid> Pending
	{
		get
		{
			lock (_gate)
			{
				return [.. _pending];
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
			_persisted = [.. _uiState.Current.RestoreConnectionIds];
			if (Mode != SessionRestoreMode.Off)
			{
				_pending = [.. _persisted];
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
	}

	/// <summary>Drops a session the user closed, so the next start does not bring it back.</summary>
	public void Forget(Guid sessionId)
	{
		lock (_gate)
		{
			if (_disposed || _entries.RemoveAll(entry => entry.SessionId == sessionId) == 0)
			{
				return;
			}
		}

		Persist();
	}

	/// <summary>Opens every login in the saved set, in the order their tabs had.</summary>
	public async Task RestoreAsync()
	{
		List<Guid> ids = TakePending();
		if (ids.Count == 0)
		{
			return;
		}

		int failed = 0;
		foreach (Guid connectionId in ids)
		{
			try
			{
				await _manager.OpenAsync(connectionId);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				// A login deleted since the last run is the usual reason, and one bad id must not stop the rest.
				failed++;
				_logger.LogWarning(ex, "Reopening connection {ConnectionId} failed.", connectionId);
			}
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
		if (TakePending().Count == 0)
		{
			return;
		}

		Persist();
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

	private List<Guid> TakePending()
	{
		List<Guid> ids;
		lock (_gate)
		{
			ids = _pending;
			_pending = [];
		}

		if (ids.Count > 0)
		{
			Changed?.Invoke();
		}

		return ids;
	}

	private void OnWorkspaceChanged()
	{
		IReadOnlyList<ISessionHandle> tabs = _workspace.Tabs;
		bool offerGone = false;
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			List<RestoreEntry> next = [];
			HashSet<Guid> open = [];
			foreach (ISessionHandle session in tabs)
			{
				// A quick-connect tab has no saved login to reopen.
				if (session.IsTransient || !open.Add(session.Id))
				{
					continue;
				}

				next.Add(new RestoreEntry(session.Id, session.Connection.Id));
			}

			// Tabs that went away without a close stay, after the open ones: that is a window closing, not a user.
			next.AddRange(_entries.Where(entry => !open.Contains(entry.SessionId)));
			_entries.Clear();
			_entries.AddRange(next);

			if (_pending.Count > 0 && open.Count > 0)
			{
				// The user got going on their own, so the offer is stale.
				_pending = [];
				offerGone = true;
			}
		}

		if (offerGone)
		{
			Changed?.Invoke();
		}

		Persist();
	}

	private void Persist()
	{
		List<Guid> ids;
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			ids = [.. _pending];
			ids.AddRange(_entries.Select(entry => entry.ConnectionId));
			if (ids.SequenceEqual(_persisted))
			{
				return;
			}

			_persisted = ids;
		}

		_uiState.Update(state => state with { RestoreConnectionIds = ids });
	}

	private sealed record RestoreEntry(Guid SessionId, Guid ConnectionId);
}
