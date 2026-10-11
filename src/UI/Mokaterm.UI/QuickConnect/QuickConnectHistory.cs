using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Storage;

namespace Mokaterm.UI.QuickConnect;

/// <summary>
/// The targets quick connect opened, newest first, in an encrypted vault document of their own and never in settings
/// or UI state: every entry names a machine. A target is kept only once its session actually connected, so a typo
/// never becomes a suggestion. While the vault is locked the history reads as empty and nothing is written.
/// </summary>
internal sealed class QuickConnectHistory : IDisposable
{
	/// <summary>Enough to cover the machines someone reaches without saving them; past that a saved login is the answer.</summary>
	public const int MaxEntries = 20;

	// Every window and circuit has a history of its own, and a change reads the document, changes it and writes it back.
	private static readonly SemaphoreSlim UpdateLock = new(1, 1);

	private readonly IVaultDataStore _vault;
	private readonly ISessionManager _sessions;
	private readonly TimeProvider _time;
	private readonly ILogger<QuickConnectHistory> _logger;
	private readonly Lock _gate = new();
	private readonly Dictionary<Guid, PendingRecord> _pending = [];
	private bool _disposed;

	public QuickConnectHistory(IVaultDataStore vault, ISessionManager sessions, TimeProvider time, ILogger<QuickConnectHistory> logger)
	{
		_vault = vault;
		_sessions = sessions;
		_time = time;
		_logger = logger;
		_vault.DocumentChanged += OnDocumentChanged;
		_sessions.SessionsChanged += OnSessionsChanged;
	}

	/// <summary>Raised after the history changed, in this window or another one. Handlers may run on any thread.</summary>
	public event Action? Changed;

	/// <summary>Sessions still waiting to connect before their target is kept. For tests.</summary>
	internal int PendingCount
	{
		get
		{
			lock (_gate)
			{
				return _pending.Count;
			}
		}
	}

	/// <summary>The kept targets, newest first, or none while the vault is locked.</summary>
	public async ValueTask<IReadOnlyList<QuickConnectRecent>> ListAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			QuickConnectHistoryDocument? document =
				await _vault.ReadAsync<QuickConnectHistoryDocument>(QuickConnectHistoryDocument.DocumentName, cancellationToken);
			return document is null ? [] : [.. document.Entries];
		}
		catch (VaultLockedException)
		{
			return [];
		}
	}

	/// <summary>
	/// Puts <paramref name="entry"/> first, replacing an older entry for the same target, and drops whatever falls past
	/// <see cref="MaxEntries"/>. Does nothing while the vault is locked.
	/// </summary>
	public Task RecordAsync(QuickConnectRecent entry, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(entry);
		return UpdateAsync(
			entries =>
			{
				entries.RemoveAll(entry.IsSameTarget);
				entries.Insert(0, entry);
				if (entries.Count > MaxEntries)
				{
					entries.RemoveRange(MaxEntries, entries.Count - MaxEntries);
				}
			},
			cancellationToken);
	}

	/// <summary>Forgets the entry for <paramref name="entry"/>'s target. Does nothing while the vault is locked.</summary>
	public Task RemoveAsync(QuickConnectRecent entry, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(entry);
		return UpdateAsync(entries => entries.RemoveAll(entry.IsSameTarget), cancellationToken);
	}

	/// <summary>Forgets every entry. Does nothing while the vault is locked.</summary>
	public Task ClearAsync(CancellationToken cancellationToken = default) => UpdateAsync(entries => entries.Clear(), cancellationToken);

	/// <summary>
	/// Keeps <paramref name="entry"/> once <paramref name="session"/> connects, which may be after a reconnect. A session
	/// that closes first, or that was saved as a login meanwhile, leaves nothing behind.
	/// </summary>
	public void RecordWhenConnected(ISessionHandle session, QuickConnectRecent entry)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(entry);
		PendingRecord pending = new(this, session, entry);
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			if (_pending.Remove(session.Id, out PendingRecord? previous))
			{
				previous.Detach();
			}

			_pending[session.Id] = pending;
		}

		session.Changed += pending.OnChanged;

		// The session may have connected before the handler was in place.
		pending.OnChanged();
	}

	public void Dispose()
	{
		List<PendingRecord> pending;
		lock (_gate)
		{
			_disposed = true;
			pending = [.. _pending.Values];
			_pending.Clear();
		}

		foreach (PendingRecord record in pending)
		{
			record.Detach();
		}

		_vault.DocumentChanged -= OnDocumentChanged;
		_sessions.SessionsChanged -= OnSessionsChanged;
	}

	private async Task UpdateAsync(Action<List<QuickConnectRecent>> change, CancellationToken cancellationToken)
	{
		await UpdateLock.WaitAsync(cancellationToken);
		try
		{
			QuickConnectHistoryDocument document =
				await _vault.ReadAsync<QuickConnectHistoryDocument>(QuickConnectHistoryDocument.DocumentName, cancellationToken)
				?? new QuickConnectHistoryDocument();
			List<QuickConnectRecent> entries = [.. document.Entries];
			change(entries);
			if (entries.Count == 0)
			{
				await _vault.DeleteAsync(QuickConnectHistoryDocument.DocumentName, cancellationToken);
			}
			else
			{
				document.Entries = entries;
				await _vault.WriteAsync(QuickConnectHistoryDocument.DocumentName, document, cancellationToken);
			}
		}
		catch (VaultLockedException)
		{
			// Nothing is kept or forgotten while the vault is locked; the history comes back as it was on unlock.
		}
		finally
		{
			UpdateLock.Release();
		}
	}

	private void Complete(PendingRecord pending)
	{
		lock (_gate)
		{
			if (!_pending.TryGetValue(pending.Session.Id, out PendingRecord? current) || !ReferenceEquals(current, pending))
			{
				return;
			}

			_pending.Remove(pending.Session.Id);
		}

		pending.Detach();

		// Saved as a login before it connected: the login is what the box offers from now on.
		if (!pending.Session.IsTransient)
		{
			return;
		}

		QuickConnectRecent entry = new()
		{
			ProtocolId = pending.Entry.ProtocolId,
			Username = pending.Entry.Username,
			Address = pending.Entry.Address,
			Port = pending.Entry.Port,
			ConnectedAt = _time.GetUtcNow(),
		};

		_ = RecordQuietlyAsync(entry);
	}

	private async Task RecordQuietlyAsync(QuickConnectRecent entry)
	{
		try
		{
			await RecordAsync(entry);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			// The history is a convenience: a vault that cannot be written must not reach the session that just opened.
			_logger.LogWarning("Keeping a quick connect target failed: {Failure}", LogSafe.Describe(ex));
		}
	}

	// A closed tab's session never connects; its handler goes with it.
	private void OnSessionsChanged()
	{
		List<PendingRecord> gone = [];
		HashSet<Guid> open = [.. _sessions.Sessions.Select(session => session.Id)];
		lock (_gate)
		{
			foreach ((Guid id, PendingRecord pending) in _pending)
			{
				if (!open.Contains(id))
				{
					gone.Add(pending);
				}
			}

			foreach (PendingRecord pending in gone)
			{
				_pending.Remove(pending.Session.Id);
			}
		}

		foreach (PendingRecord pending in gone)
		{
			pending.Detach();
		}
	}

	private void OnDocumentChanged(string name)
	{
		if (name == QuickConnectHistoryDocument.DocumentName)
		{
			Changed?.Invoke();
		}
	}

	/// <summary>One session's wait for its first connect.</summary>
	private sealed class PendingRecord(QuickConnectHistory history, ISessionHandle session, QuickConnectRecent entry)
	{
		public ISessionHandle Session => session;

		public QuickConnectRecent Entry => entry;

		public void OnChanged()
		{
			if (session.State == SessionState.Connected)
			{
				history.Complete(this);
			}
		}

		public void Detach() => session.Changed -= OnChanged;
	}
}
