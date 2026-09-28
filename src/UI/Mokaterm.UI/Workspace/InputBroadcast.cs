using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.UI.Workspace;

/// <summary>
/// The sessions that type together: what is sent in one member session is repeated in every other member. Membership is
/// per session and off by default, because one keystroke reaching several machines is worth an explicit switch.
/// </summary>
internal sealed class InputBroadcast : IDisposable
{
	private readonly SessionWorkspace _workspace;
	private readonly Lock _gate = new();
	private readonly HashSet<Guid> _members = [];
	private bool _disposed;

	public InputBroadcast(SessionWorkspace workspace)
	{
		_workspace = workspace;
		_workspace.Changed += OnWorkspaceChanged;
	}

	/// <summary>Raised when a session joins or leaves. Any thread.</summary>
	public event Action? Changed;

	/// <summary>How many sessions type together.</summary>
	public int Count
	{
		get
		{
			lock (_gate)
			{
				return _members.Count;
			}
		}
	}

	public bool IsMember(Guid sessionId)
	{
		lock (_gate)
		{
			return _members.Contains(sessionId);
		}
	}

	/// <summary>Adds or removes a session, returning whether it types with the others now.</summary>
	public bool Toggle(Guid sessionId)
	{
		bool member;
		lock (_gate)
		{
			member = !_members.Remove(sessionId);
			if (member)
			{
				_members.Add(sessionId);
			}
		}

		Changed?.Invoke();
		return member;
	}

	/// <summary>The open terminals of the other members, for repeating what <paramref name="senderId"/> just sent.</summary>
	public IReadOnlyList<ITerminalStream> OthersFor(Guid senderId)
	{
		List<Guid> ids;
		lock (_gate)
		{
			if (_members.Count < 2 || !_members.Contains(senderId))
			{
				return [];
			}

			ids = [.. _members.Where(id => id != senderId)];
		}

		List<ITerminalStream> streams = [];
		foreach (Guid id in ids)
		{
			// A session that is connecting or dropped has nothing to type into; it rejoins on its own once it is back.
			if (_workspace.Find(id)?.Terminal is { IsOpen: true } stream)
			{
				streams.Add(stream);
			}
		}

		return streams;
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
			_members.Clear();
		}

		_workspace.Changed -= OnWorkspaceChanged;
	}

	private void OnWorkspaceChanged()
	{
		HashSet<Guid> open = [.. _workspace.Tabs.Select(session => session.Id)];
		bool changed;
		lock (_gate)
		{
			// A closed tab leaves, so reopening the same login never starts out typing into everything.
			changed = _members.RemoveWhere(id => !open.Contains(id)) > 0;
		}

		if (changed)
		{
			Changed?.Invoke();
		}
	}
}
