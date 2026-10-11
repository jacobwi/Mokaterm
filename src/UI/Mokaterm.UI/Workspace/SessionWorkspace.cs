using Mokaterm.Abstractions.Sessions;

namespace Mokaterm.UI.Workspace;

/// <summary>
/// The shell's view of open sessions: which pane a tab sits in, how the panes are laid out, tab order, the active tab of
/// each pane and per-session view state. It follows <see cref="ISessionManager"/> for the whole scope, so tabs keep
/// their order and state while the vault is locked.
/// </summary>
/// <remarks>
/// A pane lives only while it holds a tab. One that runs out leaves the tree and whatever sat beside it takes the space,
/// so an empty pane never takes up part of the window and two panes are just the smallest case of the rule. The one
/// exception is a restore in progress, which lays out every pane it is about to fill before the first tab arrives, so
/// no view has to move once it is up.
/// </remarks>
internal sealed class SessionWorkspace : IDisposable
{
	/// <summary>The most panes the workspace splits into.</summary>
	public const int MaxPanes = 4;

	private readonly ISessionManager _manager;
	private readonly Lock _gate = new();
	private readonly List<ISessionHandle> _openOrder = [];
	private readonly List<ISessionHandle> _tabOrder = [];
	private readonly Dictionary<Guid, SessionViewState> _viewStates = [];
	private readonly Dictionary<Guid, int> _panes = [];
	private readonly Dictionary<int, Guid> _activeByPane = [];
	private readonly List<(Guid ConnectionId, int PaneId)> _placements = [];
	private PaneNode _layout;
	private int _focusedPane;
	private int _nextId;
	private bool _restoring;
	private Guid? _pendingActivation;
	private bool _disposed;

	public SessionWorkspace(ISessionManager manager)
	{
		_manager = manager;
		_focusedPane = NextId();
		_layout = new PaneLeaf(_focusedPane);
		_manager.SessionsChanged += Synchronize;
		Synchronize();
	}

	/// <summary>Raised when sessions open, close or change, and when tabs move, activate or change pane. Any thread.</summary>
	public event Action? Changed;

	/// <summary>Sessions in tab order, every pane together.</summary>
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

	/// <summary>How the panes are laid out. A single pane when nothing is split.</summary>
	public PaneNode Layout
	{
		get
		{
			lock (_gate)
			{
				return _layout;
			}
		}
	}

	/// <summary>The pane ids in order, which is the order the commands number them in.</summary>
	public IReadOnlyList<int> Panes
	{
		get
		{
			lock (_gate)
			{
				return PaneLayout.Panes(_layout);
			}
		}
	}

	public int PaneCount
	{
		get
		{
			lock (_gate)
			{
				return PaneLayout.Panes(_layout).Count;
			}
		}
	}

	/// <summary>Whether more than one pane is on screen.</summary>
	public bool IsSplit => PaneCount > 1;

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
				return ActiveIdInLocked(_focusedPane);
			}
		}
	}

	public ISessionHandle? Active
	{
		get
		{
			lock (_gate)
			{
				return ActiveIdInLocked(_focusedPane) is { } id ? _tabOrder.Find(session => session.Id == id) : null;
			}
		}
	}

	/// <summary>Every tab with its pane, and the layout, read together.</summary>
	public WorkspaceSnapshot Snapshot()
	{
		lock (_gate)
		{
			return new WorkspaceSnapshot([.. _tabOrder.Select(session => new PanedTab(session, PaneOfLocked(session.Id)))], _layout);
		}
	}

	/// <summary>The tabs of one pane, in tab order.</summary>
	public IReadOnlyList<ISessionHandle> TabsIn(int paneId)
	{
		lock (_gate)
		{
			return TabsInLocked(paneId);
		}
	}

	public Guid? ActiveIdIn(int paneId)
	{
		lock (_gate)
		{
			return ActiveIdInLocked(paneId);
		}
	}

	/// <summary>The id of the pane a session's tab sits in, or -1 for a session that has no tab.</summary>
	public int PaneOf(Guid sessionId)
	{
		lock (_gate)
		{
			return PaneOfLocked(sessionId);
		}
	}

	/// <summary>Where a pane comes in the order, counting from 0, or -1 for a pane that is not on screen.</summary>
	public int PositionOf(int paneId)
	{
		lock (_gate)
		{
			return PaneLayout.IndexOf(PaneLayout.Panes(_layout), paneId);
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
			List<ISessionHandle> pane = TabsInLocked(_focusedPane);
			if (pane.Count == 0)
			{
				return;
			}

			int index = ActiveIdInLocked(_focusedPane) is { } id ? pane.FindIndex(session => session.Id == id) : -1;
			int next = ((Math.Max(index, 0) + offset) % pane.Count + pane.Count) % pane.Count;
			_activeByPane[_focusedPane] = pane[next].Id;
		}

		Changed?.Invoke();
	}

	/// <summary>Points the commands at a pane, which is what clicking in it does.</summary>
	public void FocusPane(int paneId)
	{
		lock (_gate)
		{
			if (_focusedPane == paneId || !PaneLayout.Contains(_layout, paneId) || !HasTabsLocked(paneId))
			{
				return;
			}

			_focusedPane = paneId;
		}

		Changed?.Invoke();
	}

	/// <summary>
	/// Points the commands at the pane at <paramref name="position"/> and returns its active tab, so the caller can put
	/// the keyboard there too. Null when there is no such pane.
	/// </summary>
	public Guid? FocusPaneAt(int position)
	{
		bool changed;
		Guid? active;
		lock (_gate)
		{
			IReadOnlyList<int> panes = PaneLayout.Panes(_layout);
			if (position < 0 || position >= panes.Count || ActiveIdInLocked(panes[position]) is not { } id)
			{
				return null;
			}

			changed = _focusedPane != panes[position];
			_focusedPane = panes[position];
			active = id;
		}

		if (changed)
		{
			Changed?.Invoke();
		}

		return active;
	}

	/// <summary>
	/// Moves a tab on to the next pane, wrapping around, or into a new pane on the right when there is only one. With two
	/// panes that is the other one, which is how the split opens and closes.
	/// </summary>
	public void MoveToOtherPane(Guid sessionId)
	{
		bool moved;
		lock (_gate)
		{
			if (!_panes.TryGetValue(sessionId, out int from))
			{
				return;
			}

			IReadOnlyList<int> panes = PaneLayout.Panes(_layout);
			moved = panes.Count == 1
				? MoveToNewPaneLocked(sessionId, from, PaneDirection.Right)
				: MoveToPaneLocked(sessionId, panes[(PaneLayout.IndexOf(panes, from) + 1) % panes.Count]);
		}

		if (moved)
		{
			Changed?.Invoke();
		}
	}

	/// <summary>
	/// Moves a tab to the pane at <paramref name="position"/>. A position past the last pane opens one more and lays the
	/// panes out the standard way for the new count (two side by side, then one large and two stacked, then a grid).
	/// False when nothing moved: the pane is the tab's own, the workspace is full, or the tab would leave its pane empty
	/// only to fill a new one in the same place.
	/// </summary>
	public bool MoveToPane(Guid sessionId, int position)
	{
		bool moved;
		lock (_gate)
		{
			if (position < 0 || !_panes.TryGetValue(sessionId, out int from))
			{
				return false;
			}

			IReadOnlyList<int> panes = PaneLayout.Panes(_layout);
			if (position < panes.Count)
			{
				moved = MoveToPaneLocked(sessionId, panes[position]);
			}
			else if (panes.Count >= MaxPanes || TabsInLocked(from).Count < 2)
			{
				moved = false;
			}
			else
			{
				int added = NextId();
				_layout = Grow(panes, added);
				moved = MoveToPaneLocked(sessionId, added);
			}
		}

		if (moved)
		{
			Changed?.Invoke();
		}

		return moved;
	}

	/// <summary>
	/// Moves a tab into a new pane on the <paramref name="direction"/> side of its own. False when the workspace already
	/// holds <see cref="MaxPanes"/> panes or the tab is alone in its pane, where the new pane would only take the old one's
	/// place.
	/// </summary>
	public bool MoveToNewPane(Guid sessionId, PaneDirection direction)
	{
		bool moved;
		lock (_gate)
		{
			moved = _panes.TryGetValue(sessionId, out int from) && MoveToNewPaneLocked(sessionId, from, direction);
		}

		if (moved)
		{
			Changed?.Invoke();
		}

		return moved;
	}

	/// <summary>
	/// Lays the tabs out in a named layout and returns how many panes it ended up with. Panes the layout has no room for
	/// fold into its last one; a pane the layout adds takes one tab from the pane with the most, never that pane's active
	/// tab, and a layout that asks for more panes than there are tabs to fill them gets fewer. The tab the commands acted
	/// on keeps the focus wherever it lands. <see cref="PaneArrangement.Single"/> keeps the focused pane and gathers every
	/// tab into it.
	/// </summary>
	public int Arrange(PaneArrangement arrangement)
	{
		int count;
		bool changed;
		lock (_gate)
		{
			if (_tabOrder.Count == 0)
			{
				return PaneLayout.Panes(_layout).Count;
			}

			PaneNode before = _layout;
			Dictionary<Guid, int> panesBefore = new(_panes);
			Guid? active = ActiveIdInLocked(_focusedPane);
			IReadOnlyList<int> panes = PaneLayout.Panes(_layout);
			int wanted = PaneLayout.PaneCount(arrangement);
			List<int> ids = arrangement == PaneArrangement.Single ? [_focusedPane] : [.. panes.Take(wanted)];
			while (ids.Count < wanted)
			{
				ids.Add(NextId());
			}

			foreach (ISessionHandle session in _tabOrder)
			{
				if (!ids.Contains(PaneOfLocked(session.Id)))
				{
					_panes[session.Id] = ids[^1];
				}
			}

			for (int index = panes.Count; index < wanted; index++)
			{
				if (!FillLocked(ids[index], ids.Take(index)))
				{
					break;
				}
			}

			PaneNode arranged = PaneLayout.Build(arrangement, ids, NextId);
			if (PaneLayout.Write(arranged) != PaneLayout.Write(_layout) || !PaneLayout.Panes(_layout).SequenceEqual(ids))
			{
				_layout = arranged;
			}

			if (active is { } kept)
			{
				_focusedPane = PaneOfLocked(kept);
				_activeByPane[_focusedPane] = kept;
			}

			SettleLocked();
			count = PaneLayout.Panes(_layout).Count;
			changed = !ReferenceEquals(before, _layout) || !SamePanes(panesBefore);
		}

		if (changed)
		{
			Changed?.Invoke();
		}

		return count;
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

			List<ISessionHandle> tabs = TabsInLocked(PaneOfLocked(sessionId));
			int current = tabs.FindIndex(session => session.Id == sessionId);
			int target = Math.Clamp(newIndex, 0, tabs.Count - 1);
			if (current < 0 || current == target)
			{
				return;
			}

			ISessionHandle moving = _tabOrder[index];
			ISessionHandle anchor = tabs[target];
			_tabOrder.RemoveAt(index);

			// Land beside the tab that holds the target slot in this pane, so the other panes' tabs keep their order.
			int anchorIndex = _tabOrder.FindIndex(session => session.Id == anchor.Id);
			_tabOrder.Insert(target > current ? anchorIndex + 1 : anchorIndex, moving);
		}

		Changed?.Invoke();
	}

	/// <summary>
	/// Lays out the panes a restore is about to fill, from a layout <see cref="PaneLayout.Write"/> wrote, and remembers
	/// which pane each login's session goes to. Until <see cref="EndRestore"/> the panes stay even while empty, so each
	/// view mounts once, in its own pane. Does nothing when the layout cannot be read or the workspace is already split:
	/// then every session joins the focused pane, which is what a restore did before panes were saved.
	/// </summary>
	public void BeginRestore(string? layout, IReadOnlyList<PanePlacement> placements)
	{
		ArgumentNullException.ThrowIfNull(placements);
		lock (_gate)
		{
			IReadOnlyList<int> current = PaneLayout.Panes(_layout);
			if (_restoring || _disposed || current.Count > 1)
			{
				return;
			}

			// The pane on screen becomes the first one, so a quick-connect tab that is already open stays where it is.
			bool first = true;
			PaneNode? restored = PaneLayout.Read(
				layout,
				MaxPanes,
				() =>
				{
					if (!first)
					{
						return NextId();
					}

					first = false;
					return current[0];
				},
				NextId);
			if (restored is null)
			{
				return;
			}

			IReadOnlyList<int> panes = PaneLayout.Panes(restored);
			_layout = restored;
			_restoring = true;
			_placements.Clear();
			foreach (PanePlacement placement in placements)
			{
				int position = placement.Position >= 0 && placement.Position < panes.Count ? placement.Position : 0;
				_placements.Add((placement.ConnectionId, panes[position]));
			}
		}

		Changed?.Invoke();
	}

	/// <summary>Ends a restore: panes it left empty, a login that could not be opened among them, go away.</summary>
	public void EndRestore()
	{
		bool changed;
		lock (_gate)
		{
			if (!_restoring)
			{
				return;
			}

			_restoring = false;
			_placements.Clear();
			changed = SettleLocked();
		}

		if (changed)
		{
			Changed?.Invoke();
		}
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
			_activeByPane.Clear();
			_placements.Clear();
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

				if (ActiveIdInLocked(pane) == session.Id)
				{
					// Closing the active tab activates its right-hand neighbour in the same pane, like most tabbed apps.
					List<ISessionHandle> tabs = TabsInLocked(pane);
					if (tabs.Count == 0)
					{
						_activeByPane.Remove(pane);
					}
					else
					{
						_activeByPane[pane] = tabs[Math.Clamp(CountBefore(tabIndex, pane), 0, tabs.Count - 1)].Id;
					}
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

					// A new session joins the pane the user is working in, unless a restore is putting it back in its own.
					_panes[session.Id] = TakePlacementLocked(session.Connection.Id) ?? _focusedPane;
					added.Add(session);
					changed = true;
				}
			}

			// A pending id that never shows up is harmless: session ids are unique, so it can never match another session.
			if (_pendingActivation is { } pending && knownIds.Contains(pending))
			{
				int pane = PaneOfLocked(pending);
				changed |= ActiveIdInLocked(pane) != pending || _focusedPane != pane;
				_activeByPane[pane] = pending;
				_focusedPane = pane;
				_pendingActivation = null;
			}

			changed |= SettleLocked();
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

	private int NextId() => _nextId++;

	private int PaneOfLocked(Guid sessionId) => _panes.TryGetValue(sessionId, out int pane) ? pane : -1;

	private Guid? ActiveIdInLocked(int paneId) => _activeByPane.TryGetValue(paneId, out Guid id) ? id : null;

	private bool HasTabsLocked(int paneId) => _tabOrder.Exists(session => PaneOfLocked(session.Id) == paneId);

	private List<ISessionHandle> TabsInLocked(int paneId) => [.. _tabOrder.Where(session => PaneOfLocked(session.Id) == paneId)];

	private bool SamePanes(Dictionary<Guid, int> before) =>
		before.Count == _panes.Count && before.All(entry => _panes.TryGetValue(entry.Key, out int pane) && pane == entry.Value);

	private bool MoveToPaneLocked(Guid sessionId, int target)
	{
		if (!_panes.TryGetValue(sessionId, out int from) || from == target || !PaneLayout.Contains(_layout, target))
		{
			return false;
		}

		_panes[sessionId] = target;
		_activeByPane[target] = sessionId;
		_focusedPane = target;
		if (ActiveIdInLocked(from) == sessionId)
		{
			ISessionHandle? next = _tabOrder.Find(session => session.Id != sessionId && PaneOfLocked(session.Id) == from);
			if (next is null)
			{
				_activeByPane.Remove(from);
			}
			else
			{
				_activeByPane[from] = next.Id;
			}
		}

		SettleLocked();
		return true;
	}

	private bool MoveToNewPaneLocked(Guid sessionId, int from, PaneDirection direction)
	{
		if (PaneLayout.Panes(_layout).Count >= MaxPanes || TabsInLocked(from).Count < 2)
		{
			return false;
		}

		int added = NextId();
		_layout = PaneLayout.Split(_layout, from, direction, added, NextId());
		return MoveToPaneLocked(sessionId, added);
	}

	/// <summary>
	/// The standard layout for one more pane, built by splitting the last pane where that gets there, so the splits that
	/// stay keep their ids and their views stay mounted.
	/// </summary>
	private PaneNode Grow(IReadOnlyList<int> panes, int added)
	{
		List<int> ids = [.. panes, added];
		PaneNode standard = PaneLayout.Build(PaneLayout.ForCount(ids.Count), ids, NextId);
		string shape = PaneLayout.Write(standard);
		foreach (PaneDirection direction in (ReadOnlySpan<PaneDirection>)[PaneDirection.Right, PaneDirection.Down])
		{
			PaneNode split = PaneLayout.Split(_layout, panes[^1], direction, added, NextId());
			if (PaneLayout.Write(split) == shape)
			{
				return split;
			}
		}

		return standard;
	}

	/// <summary>Gives an empty new pane one tab from the fullest of <paramref name="donors"/>. False when none can spare one.</summary>
	private bool FillLocked(int paneId, IEnumerable<int> donors)
	{
		int donor = -1;
		List<ISessionHandle> tabs = [];
		foreach (int candidate in donors)
		{
			List<ISessionHandle> candidateTabs = TabsInLocked(candidate);
			if (candidateTabs.Count > tabs.Count)
			{
				donor = candidate;
				tabs = candidateTabs;
			}
		}

		if (tabs.Count < 2)
		{
			return false;
		}

		Guid? keep = ActiveIdInLocked(donor);
		ISessionHandle moving = tabs.Find(session => session.Id != keep) ?? tabs[^1];
		_panes[moving.Id] = paneId;
		_activeByPane[paneId] = moving.Id;
		return true;
	}

	private int? TakePlacementLocked(Guid connectionId)
	{
		int index = _placements.FindIndex(placement => placement.ConnectionId == connectionId);
		if (index < 0)
		{
			return null;
		}

		int pane = _placements[index].PaneId;
		_placements.RemoveAt(index);
		return PaneLayout.Contains(_layout, pane) ? pane : null;
	}

	/// <summary>
	/// Puts the rules back after any change: no empty pane outside a restore, an active tab in every pane that has tabs,
	/// and the focus on a pane with tabs whenever one has any. Returns whether anything changed.
	/// </summary>
	private bool SettleLocked()
	{
		PaneNode layout = _layout;
		int focused = _focusedPane;
		Dictionary<int, Guid> actives = new(_activeByPane);

		if (!_restoring)
		{
			// The pane that takes the focused one's space takes its focus too.
			int? neighbour = HasTabsLocked(_focusedPane) ? null : PaneLayout.Neighbour(_layout, _focusedPane);
			if (PaneLayout.Prune(_layout, HasTabsLocked) is { } pruned)
			{
				_layout = pruned;
			}
			else if (_layout is not PaneLeaf)
			{
				// Nothing has a tab: one pane stays for the welcome screen, the focused one when it is still there.
				_layout = new PaneLeaf(PaneLayout.Contains(_layout, _focusedPane) ? _focusedPane : PaneLayout.Panes(_layout)[0]);
			}

			if (neighbour is { } next && PaneLayout.Contains(_layout, next))
			{
				_focusedPane = next;
			}
		}

		IReadOnlyList<int> panes = PaneLayout.Panes(_layout);
		foreach (int gone in _activeByPane.Keys.Where(pane => PaneLayout.IndexOf(panes, pane) < 0).ToList())
		{
			_activeByPane.Remove(gone);
		}

		foreach (int pane in panes)
		{
			List<ISessionHandle> tabs = TabsInLocked(pane);
			if (tabs.Count == 0)
			{
				_activeByPane.Remove(pane);
			}
			else if (ActiveIdInLocked(pane) is not { } active || !tabs.Exists(session => session.Id == active))
			{
				_activeByPane[pane] = tabs[^1].Id;
			}
		}

		// An empty pane cannot hold the commands' idea of "the active session".
		if (PaneLayout.IndexOf(panes, _focusedPane) < 0 || !HasTabsLocked(_focusedPane))
		{
			int withTabs = -1;
			foreach (int pane in panes)
			{
				if (HasTabsLocked(pane))
				{
					withTabs = pane;
					break;
				}
			}

			_focusedPane = withTabs >= 0 ? withTabs : PaneLayout.IndexOf(panes, _focusedPane) >= 0 ? _focusedPane : panes[0];
		}

		return !ReferenceEquals(layout, _layout)
			|| focused != _focusedPane
			|| actives.Count != _activeByPane.Count
			|| actives.Any(entry => ActiveIdInLocked(entry.Key) != entry.Value);
	}

	/// <summary>How many tabs of <paramref name="paneId"/> sit before <paramref name="tabIndex"/> in the shared order.</summary>
	private int CountBefore(int tabIndex, int paneId)
	{
		int count = 0;
		for (int i = 0; i < tabIndex && i < _tabOrder.Count; i++)
		{
			if (PaneOfLocked(_tabOrder[i].Id) == paneId)
			{
				count++;
			}
		}

		return count;
	}

	private void OnSessionChanged() => Changed?.Invoke();
}
