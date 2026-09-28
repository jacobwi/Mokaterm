using Mokaterm.Abstractions.Sessions;

namespace Mokaterm.UI.Workspace;

/// <summary>
/// The shell's view of open sessions: which pane a tab sits in, tab order, the active tab of each pane and per-session
/// view state. It follows <see cref="ISessionManager"/> for the whole scope, so tabs keep their order and state while
/// the vault is locked.
/// </summary>
internal sealed class SessionWorkspace : IDisposable
{
	/// <summary>The workspace shows at most two panes side by side, like an editor's split.</summary>
	public const int PaneCount = 2;

	private readonly ISessionManager _manager;
	private readonly Lock _gate = new();
	private readonly List<ISessionHandle> _openOrder = [];
	private readonly List<ISessionHandle> _tabOrder = [];
	private readonly Dictionary<Guid, SessionViewState> _viewStates = [];
	private readonly Dictionary<Guid, int> _panes = [];
	private readonly Guid?[] _activeByPane = new Guid?[PaneCount];
	private int _focusedPane;
	private Guid? _pendingActivation;
	private bool _disposed;

	public SessionWorkspace(ISessionManager manager)
	{
		_manager = manager;
		_manager.SessionsChanged += Synchronize;
		Synchronize();
	}

	/// <summary>Raised when sessions open, close or change, and when tabs move, activate or change pane. Any thread.</summary>
	public event Action? Changed;

	/// <summary>Sessions in tab order, both panes together.</summary>
	public IReadOnlyList<ISessionHandle> Tabs
	{
		get
		{
			lock (_gate)
			{
				return [.. _tabOrder];
			}
		}
	}

	/// <summary>
	/// Sessions in the order they were opened. Views render in this order, so reordering tabs never moves a terminal's
	/// DOM.
	/// </summary>
	public IReadOnlyList<ISessionHandle> OpenOrder
	{
		get
		{
			lock (_gate)
			{
				return [.. _openOrder];
			}
		}
	}

	/// <summary>
	/// Whether both panes hold tabs. The split opens with the first tab moved across and closes when one side runs out,
	/// so an empty pane never takes up half the window.
	/// </summary>
	public bool IsSplit
	{
		get
		{
			lock (_gate)
			{
				return _tabOrder.Exists(session => PaneOfLocked(session.Id) == 0)
					&& _tabOrder.Exists(session => PaneOfLocked(session.Id) == 1);
			}
		}
	}

	/// <summary>The pane whose active tab the commands act on.</summary>
	public int FocusedPane
	{
		get
		{
			lock (_gate)
			{
				return _focusedPane;
			}
		}
	}

	public Guid? ActiveId
	{
		get
		{
			lock (_gate)
			{
				return _activeByPane[_focusedPane];
			}
		}
	}

	public ISessionHandle? Active
	{
		get
		{
			lock (_gate)
			{
				return _activeByPane[_focusedPane] is { } id ? _tabOrder.Find(session => session.Id == id) : null;
			}
		}
	}

	/// <summary>The tabs of one pane, in tab order.</summary>
	public IReadOnlyList<ISessionHandle> TabsIn(int pane)
	{
		lock (_gate)
		{
			return [.. _tabOrder.Where(session => PaneOfLocked(session.Id) == pane)];
		}
	}

	public Guid? ActiveIdIn(int pane)
	{
		lock (_gate)
		{
			return pane >= 0 && pane < PaneCount ? _activeByPane[pane] : null;
		}
	}

	public int PaneOf(Guid sessionId)
	{
		lock (_gate)
		{
			return PaneOfLocked(sessionId);
		}
	}

	public ISessionHandle? Find(Guid sessionId)
	{
		lock (_gate)
		{
			return _tabOrder.Find(session => session.Id == sessionId);
		}
	}

	/// <summary>Makes a session the active tab of its pane, including one whose open has not been reported yet.</summary>
	public void Activate(Guid sessionId)
	{
		lock (_gate)
		{
			_pendingActivation = sessionId;
		}

		Synchronize();
	}

	/// <summary>Activates the tab <paramref name="offset"/> positions away in the focused pane, wrapping around.</summary>
	public void ActivateRelative(int offset)
	{
		lock (_gate)
		{
			List<ISessionHandle> pane = [.. _tabOrder.Where(session => PaneOfLocked(session.Id) == _focusedPane)];
			if (pane.Count == 0)
			{
				return;
			}

			int index = _activeByPane[_focusedPane] is { } id ? pane.FindIndex(session => session.Id == id) : -1;
			int next = ((Math.Max(index, 0) + offset) % pane.Count + pane.Count) % pane.Count;
			_activeByPane[_focusedPane] = pane[next].Id;
		}

		Changed?.Invoke();
	}

	/// <summary>Points the commands at a pane, which is what clicking in it does.</summary>
	public void FocusPane(int pane)
	{
		lock (_gate)
		{
			if (pane < 0 || pane >= PaneCount || _focusedPane == pane || !_tabOrder.Exists(session => PaneOfLocked(session.Id) == pane))
			{
				return;
			}

			_focusedPane = pane;
		}

		Changed?.Invoke();
	}

	/// <summary>Moves a tab to the other pane, which is also how the split opens and closes.</summary>
	public void MoveToOtherPane(Guid sessionId)
	{
		lock (_gate)
		{
			if (!_tabOrder.Exists(session => session.Id == sessionId))
			{
				return;
			}

			int from = PaneOfLocked(sessionId);
			int to = from == 0 ? 1 : 0;
			_panes[sessionId] = to;
			_activeByPane[to] = sessionId;
			_focusedPane = to;
			if (_activeByPane[from] == sessionId)
			{
				_activeByPane[from] = _tabOrder.Find(session => session.Id != sessionId && PaneOfLocked(session.Id) == from)?.Id;
			}
		}

		Changed?.Invoke();
	}

	/// <summary>Moves a tab inside its own pane, where <paramref name="newIndex"/> counts that pane's tabs only.</summary>
	public void Move(Guid sessionId, int newIndex)
	{
		lock (_gate)
		{
			int index = _tabOrder.FindIndex(session => session.Id == sessionId);
			if (index < 0)
			{
				return;
			}

			int pane = PaneOfLocked(sessionId);
			List<ISessionHandle> tabs = [.. _tabOrder.Where(session => PaneOfLocked(session.Id) == pane)];
			int current = tabs.FindIndex(session => session.Id == sessionId);
			int target = Math.Clamp(newIndex, 0, tabs.Count - 1);
			if (current < 0 || current == target)
			{
				return;
			}

			ISessionHandle moving = _tabOrder[index];
			ISessionHandle anchor = tabs[target];
			_tabOrder.RemoveAt(index);

			// Land beside the tab that holds the target slot in this pane, so the other pane's tabs keep their order.
			int anchorIndex = _tabOrder.FindIndex(session => session.Id == anchor.Id);
			_tabOrder.Insert(target > current ? anchorIndex + 1 : anchorIndex, moving);
		}

		Changed?.Invoke();
	}

	/// <summary>The view state for a session, created with <paramref name="create"/> on first use.</summary>
	public SessionViewState GetViewState(Guid sessionId, Func<SessionViewState> create)
	{
		lock (_gate)
		{
			if (!_viewStates.TryGetValue(sessionId, out SessionViewState? state))
			{
				state = create();
				_viewStates[sessionId] = state;
			}

			return state;
		}
	}

	public string? GetRemoteTitle(Guid sessionId)
	{
		lock (_gate)
		{
			return _viewStates.TryGetValue(sessionId, out SessionViewState? state) ? state.RemoteTitle : null;
		}
	}

	public void SetRemoteTitle(Guid sessionId, string? title)
	{
		lock (_gate)
		{
			if (!_viewStates.TryGetValue(sessionId, out SessionViewState? state) || state.RemoteTitle == title)
			{
				return;
			}

			state.RemoteTitle = title;
		}

		Changed?.Invoke();
	}

	/// <summary>Raises <see cref="Changed"/> after a caller mutated a <see cref="SessionViewState"/>.</summary>
	public void NotifyViewStateChanged() => Changed?.Invoke();

	public void Dispose()
	{
		List<ISessionHandle> sessions;
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			sessions = [.. _openOrder];
			_openOrder.Clear();
			_tabOrder.Clear();
			_viewStates.Clear();
			_panes.Clear();
		}

		_manager.SessionsChanged -= Synchronize;
		foreach (ISessionHandle session in sessions)
		{
			session.Changed -= OnSessionChanged;
		}
	}

	private void Synchronize()
	{
		IReadOnlyList<ISessionHandle> current = _manager.Sessions;
		List<ISessionHandle> added = [];
		List<ISessionHandle> removed = [];
		bool changed = false;

		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			HashSet<Guid> currentIds = [.. current.Select(session => session.Id)];
			for (int i = _openOrder.Count - 1; i >= 0; i--)
			{
				ISessionHandle session = _openOrder[i];
				if (currentIds.Contains(session.Id))
				{
					continue;
				}

				int pane = PaneOfLocked(session.Id);
				_openOrder.RemoveAt(i);
				_viewStates.Remove(session.Id);
				_panes.Remove(session.Id);
				int tabIndex = _tabOrder.FindIndex(tab => tab.Id == session.Id);
				if (tabIndex >= 0)
				{
					_tabOrder.RemoveAt(tabIndex);
				}

				if (_activeByPane[pane] == session.Id)
				{
					// Closing the active tab activates its right-hand neighbour in the same pane, like most tabbed apps.
					List<ISessionHandle> tabs = [.. _tabOrder.Where(tab => PaneOfLocked(tab.Id) == pane)];
					_activeByPane[pane] = tabs.Count == 0 ? null : tabs[Math.Clamp(CountBefore(tabIndex, pane), 0, tabs.Count - 1)].Id;
				}

				removed.Add(session);
				changed = true;
			}

			HashSet<Guid> knownIds = [.. _openOrder.Select(session => session.Id)];
			foreach (ISessionHandle session in current)
			{
				if (knownIds.Add(session.Id))
				{
					_openOrder.Add(session);
					_tabOrder.Add(session);

					// A new session joins the pane the user is working in.
					_panes[session.Id] = _focusedPane;
					added.Add(session);
					changed = true;
				}
			}

			// A pending id that never shows up is harmless: session ids are unique, so it can never match another session.
			if (_pendingActivation is { } pending && knownIds.Contains(pending))
			{
				int pane = PaneOfLocked(pending);
				changed |= _activeByPane[pane] != pending || _focusedPane != pane;
				_activeByPane[pane] = pending;
				_focusedPane = pane;
				_pendingActivation = null;
			}

			for (int pane = 0; pane < PaneCount; pane++)
			{
				List<ISessionHandle> tabs = [.. _tabOrder.Where(session => PaneOfLocked(session.Id) == pane)];
				if (_activeByPane[pane] is null && tabs.Count > 0)
				{
					_activeByPane[pane] = tabs[^1].Id;
					changed = true;
				}
				else if (tabs.Count == 0 && _activeByPane[pane] is not null)
				{
					_activeByPane[pane] = null;
					changed = true;
				}
			}

			// An empty pane cannot hold the commands' idea of "the active session".
			if (_activeByPane[_focusedPane] is null && _activeByPane[_focusedPane == 0 ? 1 : 0] is not null)
			{
				_focusedPane = _focusedPane == 0 ? 1 : 0;
				changed = true;
			}
		}

		foreach (ISessionHandle session in removed)
		{
			session.Changed -= OnSessionChanged;
		}

		foreach (ISessionHandle session in added)
		{
			session.Changed += OnSessionChanged;
		}

		if (changed)
		{
			Changed?.Invoke();
		}
	}

	private int PaneOfLocked(Guid sessionId) => _panes.GetValueOrDefault(sessionId);

	/// <summary>How many tabs of <paramref name="pane"/> sit before <paramref name="tabIndex"/> in the shared order.</summary>
	private int CountBefore(int tabIndex, int pane)
	{
		int count = 0;
		for (int i = 0; i < tabIndex && i < _tabOrder.Count; i++)
		{
			if (PaneOfLocked(_tabOrder[i].Id) == pane)
			{
				count++;
			}
		}

		return count;
	}

	private void OnSessionChanged() => Changed?.Invoke();
}
