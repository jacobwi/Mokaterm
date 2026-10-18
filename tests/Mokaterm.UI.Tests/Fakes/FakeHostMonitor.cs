using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.ServerAdmin;

namespace Mokaterm.UI.Tests.Fakes;

/// <summary>
/// A session's live figures as a test sets them, counting who watches and at what pace. It reads nothing: a test that
/// wants new figures sets <see cref="State"/>, which says so the way the real feature does.
/// </summary>
internal sealed class FakeHostMonitor : IHostMonitorFeature
{
	private readonly Lock _lock = new();
	private readonly List<TimeSpan> _leases = [];
	private readonly List<TimeSpan> _asked = [];
	private HostMonitorState _state;

	public FakeHostMonitor(HostMonitorState? state = null, string? toolKey = "system")
	{
		_state = state ?? HostMonitorState.Waiting;
		ToolKey = toolKey;
	}

	public event Action? Changed;

	public HostMonitorState State
	{
		get
		{
			lock (_lock)
			{
				return _state;
			}
		}

		set
		{
			lock (_lock)
			{
				_state = value;
			}

			Changed?.Invoke();
		}
	}

	public string? ToolKey { get; }

	/// <summary>The intervals of the leases held now.</summary>
	public IReadOnlyList<TimeSpan> Leases
	{
		get
		{
			lock (_lock)
			{
				return [.. _leases];
			}
		}
	}

	/// <summary>Every interval a lease was taken with, released or not, in order.</summary>
	public IReadOnlyList<TimeSpan> Asked
	{
		get
		{
			lock (_lock)
			{
				return [.. _asked];
			}
		}
	}

	public int Subscribers => Changed?.GetInvocationList().Length ?? 0;

	public IDisposable Watch(TimeSpan interval)
	{
		lock (_lock)
		{
			_leases.Add(interval);
			_asked.Add(interval);
		}

		return new Lease(this, interval);
	}

	/// <summary>A live session whose only feature is <paramref name="monitor"/>.</summary>
	public static IProtocolSession SessionWith(FakeHostMonitor monitor) => new MonitoredSession(monitor);

	private void Release(TimeSpan interval)
	{
		lock (_lock)
		{
			_ = _leases.Remove(interval);
		}
	}

	private sealed class Lease(FakeHostMonitor owner, TimeSpan interval) : IDisposable
	{
		private bool _disposed;

		public void Dispose()
		{
			if (!_disposed)
			{
				_disposed = true;
				owner.Release(interval);
			}
		}
	}

	private sealed class MonitoredSession(FakeHostMonitor monitor) : IProtocolSession
	{
		public Task Completion => Task.CompletedTask;

		public TFeature? GetFeature<TFeature>() where TFeature : class => monitor as TFeature;

		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}
}
