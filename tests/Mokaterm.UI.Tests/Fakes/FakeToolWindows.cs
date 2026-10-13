using Mokaterm.UI.Common.Platform;

namespace Mokaterm.UI.Tests.Fakes;

/// <summary>
/// A window host that records what it was asked to do and keeps a window open from its open to its close, the way the
/// desktop's does. <see cref="CloseByUser"/> plays the window's own close button.
/// </summary>
internal sealed class FakeToolWindows : IToolWindows
{
	private readonly List<ToolWindowRequest> _shown = [];
	private readonly HashSet<ToolWindowTarget> _onTop = [];

	public event Action? Changed;

	/// <summary>False makes every open fail, as a host that cannot build a window does.</summary>
	public bool Opens { get; set; } = true;

	/// <summary>Every request that opened a window, in order; one that only brought a window forward is not here.</summary>
	public List<ToolWindowRequest> Opened { get; } = [];

	/// <summary>The windows open now.</summary>
	public IReadOnlyList<ToolWindowRequest> Shown => _shown;

	public List<ToolWindowTarget> Closed { get; } = [];

	public List<Guid> ClosedSessions { get; } = [];

	public List<IServiceProvider> ClosedOwners { get; } = [];

	public int OwnerShown { get; private set; }

	public bool IsOpen(ToolWindowTarget target) => _shown.Exists(request => request.Target == target);

	public Task<bool> OpenAsync(ToolWindowRequest request)
	{
		if (!Opens)
		{
			return Task.FromResult(false);
		}

		if (!IsOpen(request.Target))
		{
			Opened.Add(request);
			_shown.Add(request);
			if (request.Bounds?.KeepOnTop == true)
			{
				_onTop.Add(request.Target);
			}

			Changed?.Invoke();
		}

		return Task.FromResult(true);
	}

	public void Close(ToolWindowTarget target)
	{
		Closed.Add(target);
		Remove(request => request.Target == target);
	}

	public void CloseSession(Guid sessionId)
	{
		ClosedSessions.Add(sessionId);
		Remove(request => request.Target.SessionId == sessionId);
	}

	public void CloseAll(IServiceProvider services)
	{
		ClosedOwners.Add(services);
		Remove(request => ReferenceEquals(request.Services, services));
	}

	public void ShowOwner(IServiceProvider services) => OwnerShown++;

	public bool IsKeptOnTop(ToolWindowTarget target) => _onTop.Contains(target);

	public void SetKeepOnTop(ToolWindowTarget target, bool keepOnTop)
	{
		if (_shown.Find(request => request.Target == target) is not { } request)
		{
			return;
		}

		_ = keepOnTop ? _onTop.Add(target) : _onTop.Remove(target);
		request.BoundsChanged?.Invoke(new ToolWindowBounds(10, 20, 900, 600) { KeepOnTop = keepOnTop });
		Changed?.Invoke();
	}

	/// <summary>The user closes the window with its own close button.</summary>
	public void CloseByUser(ToolWindowTarget target)
	{
		if (_shown.Find(request => request.Target == target) is not { } request)
		{
			return;
		}

		Remove(shown => shown.Target == target);
		request.ClosedByUser?.Invoke();
	}

	private void Remove(Predicate<ToolWindowRequest> match)
	{
		foreach (ToolWindowRequest request in _shown.FindAll(match))
		{
			_ = _onTop.Remove(request.Target);
		}

		if (_shown.RemoveAll(match) > 0)
		{
			Changed?.Invoke();
		}
	}
}
