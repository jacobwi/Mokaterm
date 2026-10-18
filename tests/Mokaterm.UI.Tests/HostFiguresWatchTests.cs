using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.UI.Shell;
using Mokaterm.UI.Tests.Fakes;

namespace Mokaterm.UI.Tests;

/// <summary>
/// The status bar holds one lease, on the session it shows: switching tabs moves it, a drop lets it go, a reconnect
/// takes a new one on the new live session, and nothing is watched while the figures are not wanted.
/// </summary>
public sealed class HostFiguresWatchTests
{
	private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

	[Fact]
	public void AConnectedSession_GetsOneLease_HoweverOftenTheBarDraws()
	{
		FakeHostMonitor monitor = new();
		LiveHandle session = new(monitor);
		using HostFiguresWatch watch = new();

		watch.Update(session, wanted: true, Interval);
		watch.Update(session, wanted: true, Interval);

		Assert.Same(monitor, watch.Feature);
		Assert.Equal([Interval], monitor.Leases);
		Assert.Equal(1, monitor.Subscribers);
	}

	[Fact]
	public void SwitchingTabs_MovesTheLease()
	{
		FakeHostMonitor first = new();
		FakeHostMonitor second = new();
		LiveHandle a = new(first);
		LiveHandle b = new(second);
		using HostFiguresWatch watch = new();

		watch.Update(a, wanted: true, Interval);
		watch.Update(b, wanted: true, Interval);

		Assert.Empty(first.Leases);
		Assert.Equal(0, first.Subscribers);
		Assert.Equal([Interval], second.Leases);

		watch.Update(null, wanted: true, Interval);
		Assert.Empty(second.Leases);
		Assert.Null(watch.Feature);
	}

	[Fact]
	public void ADrop_LetsGo_AndAReconnect_WatchesTheNewLiveSession()
	{
		FakeHostMonitor before = new();
		LiveHandle session = new(before);
		using HostFiguresWatch watch = new();
		watch.Update(session, wanted: true, Interval);

		session.Drop();
		watch.Update(session, wanted: true, Interval);
		Assert.Empty(before.Leases);
		Assert.Null(watch.Feature);

		FakeHostMonitor after = new();
		session.Reconnect(after);
		watch.Update(session, wanted: true, Interval);
		Assert.Same(after, watch.Feature);
		Assert.Equal([Interval], after.Leases);
		Assert.Empty(before.Leases);
	}

	// Off in settings, a hidden page or a locked vault: nobody can look, so nothing is read for them.
	[Fact]
	public void NotWanted_WatchesNothing()
	{
		FakeHostMonitor monitor = new();
		LiveHandle session = new(monitor);
		using HostFiguresWatch watch = new();

		watch.Update(session, wanted: false, Interval);
		Assert.Empty(monitor.Asked);

		watch.Update(session, wanted: true, Interval);
		watch.Update(session, wanted: false, Interval);
		Assert.Empty(monitor.Leases);
		Assert.Equal(0, monitor.Subscribers);
	}

	[Fact]
	public void ANewInterval_TakesTheLeaseAgainAtThatPace()
	{
		FakeHostMonitor monitor = new();
		LiveHandle session = new(monitor);
		using HostFiguresWatch watch = new();

		watch.Update(session, wanted: true, Interval);
		watch.Update(session, wanted: true, TimeSpan.FromSeconds(30));

		Assert.Equal([TimeSpan.FromSeconds(30)], monitor.Leases);
		Assert.Equal([Interval, TimeSpan.FromSeconds(30)], monitor.Asked);
		Assert.Equal(1, monitor.Subscribers);
	}

	[Fact]
	public void ASessionWithoutFigures_WatchesNothing()
	{
		using HostFiguresWatch watch = new();

		watch.Update(new LiveHandle(null), wanted: true, Interval);

		Assert.Null(watch.Feature);
	}

	[Fact]
	public void NewFigures_ArePassedOn_AndDisposeLetsGo()
	{
		FakeHostMonitor monitor = new();
		HostFiguresWatch watch = new();
		int changes = 0;
		watch.Changed += () => changes++;
		watch.Update(new LiveHandle(monitor), wanted: true, Interval);

		monitor.State = monitor.State with { FailedReads = 1 };
		Assert.Equal(1, changes);

		watch.Dispose();
		Assert.Empty(monitor.Leases);
		monitor.State = monitor.State with { FailedReads = 2 };
		Assert.Equal(1, changes);
	}

	/// <summary>A session tab whose live session a test can drop and bring back, the way a reconnect replaces it.</summary>
	private sealed class LiveHandle : ISessionHandle
	{
		public LiveHandle(FakeHostMonitor? monitor) => Session = monitor is null ? new FeaturelessSession() : FakeHostMonitor.SessionWith(monitor);

		public event Action? Changed;

		public Guid Id { get; } = Guid.NewGuid();

		public string Title => "bc@web1";

		public HostProfile Host { get; } = new() { Id = Guid.NewGuid(), Address = "web1" };

		public ConnectionProfile Connection => new() { Id = Guid.Empty, HostId = Host.Id, ProtocolId = "ssh", Username = "bc" };

		public ProtocolDescriptor Protocol { get; } = new() { Id = "ssh", DisplayName = "SSH", DefaultPort = 22, Capabilities = ProtocolCapabilities.Terminal };

		public bool IsTransient => false;

		public SessionState State { get; private set; } = SessionState.Connected;

		public string? StatusMessage => null;

		public ConnectFailure? Failure => null;

		public DateTimeOffset OpenedAt => DateTimeOffset.UnixEpoch;

		public DateTimeOffset? ConnectedAt => DateTimeOffset.UnixEpoch;

		public ITerminalStream? Terminal => null;

		public IProtocolSession? Session { get; private set; }

		public void Drop()
		{
			State = SessionState.Disconnected;
			Session = null;
			Changed?.Invoke();
		}

		public void Reconnect(FakeHostMonitor monitor)
		{
			Session = FakeHostMonitor.SessionWith(monitor);
			State = SessionState.Connected;
			Changed?.Invoke();
		}
	}

	private sealed class FeaturelessSession : IProtocolSession
	{
		public Task Completion => Task.CompletedTask;

		public TFeature? GetFeature<TFeature>() where TFeature : class => null;

		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}
}
