using Microsoft.Extensions.Time.Testing;
using Mokaterm.Modules.Ssh.Forwarding;
using Mokaterm.Modules.Ssh.KeepAlive;
using Mokaterm.Modules.Ssh.Tests.Fakes;
using Renci.SshNet;
using Renci.SshNet.Messages.Connection;

namespace Mokaterm.Modules.Ssh.Tests.KeepAlive;

/// <summary>
/// The loop and the request line together, over a real SSH.NET session that never connects: what a server that never
/// answers gets, and what one that answered and then went silent gets. Keepalives are written on the pool, so these count
/// intervals loosely; <see cref="KeepAliveMonitorTests"/> holds the exact counting.
/// </summary>
public sealed class KeepAliveVerdictTests : IAsyncDisposable
{
	private const int MaxMissed = 3;

	private const int TickLimit = 20;

	private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

	private readonly FakeTimeProvider _time = new();
	private readonly Session _session = SshNetSession.Create();
	private readonly RecordingMessageSender _sender = new();
	private readonly GlobalRequestLine _line;
	private readonly KeepAliveMonitor _monitor;

	public KeepAliveVerdictTests()
	{
		_line = new GlobalRequestLine(_session, _sender, SshNetInternals.CreateKeepAliveRequest, TimeSpan.FromSeconds(15), _time);
		_monitor = new KeepAliveMonitor(_line, new KeepAlivePolicy(Interval, MaxMissed), _time);
	}

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	private int Asked => _sender.OfType<GlobalRequestMessage>().Count;

	public async ValueTask DisposeAsync()
	{
		await _monitor.DisposeAsync();
		_line.Dispose();
	}

	[Fact]
	public async Task AServerThatNeverAnswers_IsNeverDropped_AndAfterItsKeepAlivesItsForwardsAreNotHeld()
	{
		int ticks = await TickUntilAsync(() => _line.State.Ignored, "the line stopped asking");

		// The first keepalive goes out one interval in, and the server gets MaxMissed intervals to answer.
		Assert.True(ticks >= MaxMissed + 1, $"The line stopped asking after {ticks} intervals.");
		Assert.True(_line.StoppedAsking.IsCompleted);
		Assert.Equal(0, _line.State.Owed);
		await Eventually.TrueAsync(() => Asked >= 1, "the first keepalive was written");
		int asked = (int)await Eventually.StableAsync(() => Asked);
		Assert.InRange(asked, 1, MaxMissed);

		// A remote tunnel opens at once, without any wait.
		using ManualResetEventSlim started = new();
		await _line.StartAsync(started.Set, Ct).WaitAsync(Eventually.Limit, Ct);
		Assert.True(started.IsSet);

		// Nothing more is asked, and the connection is never dropped.
		for (int round = 0; round < 10; round++)
		{
			await TickAsync();
		}

		Assert.Equal(asked, Asked);
		Assert.False(_monitor.Lost.IsCompleted);
	}

	[Fact]
	public async Task AServerThatAnsweredOnce_ThenWentSilent_IsDroppedAfterTheMissesAllowed()
	{
		await TickAsync();
		await Eventually.TrueAsync(() => Asked == 1, "the first keepalive was written");
		_session.Receive(new RequestFailureMessage());

		int ticks = await TickUntilAsync(() => _monitor.Lost.IsCompleted, "the connection was judged lost");
		KeepAliveTimeoutException lost = await _monitor.Lost;

		// One interval to send again, then MaxMissed without an answer.
		Assert.True(ticks >= MaxMissed + 1, $"The connection was judged lost after {ticks} intervals.");
		Assert.Equal("The server stopped answering. 3 keepalives in a row went unanswered.", lost.Message);
		Assert.False(_line.State.Ignored);
	}

	private async Task TickAsync()
	{
		long before = _monitor.Intervals;
		_time.Advance(Interval);
		await Eventually.TrueAsync(() => _monitor.Intervals > before, "the keepalive loop took its turn");
	}

	/// <summary>Moves on interval by interval until <paramref name="condition"/> holds, and says how many it took.</summary>
	private async Task<int> TickUntilAsync(Func<bool> condition, string description)
	{
		for (int ticks = 1; ticks <= TickLimit; ticks++)
		{
			long before = _monitor.Intervals;
			_time.Advance(Interval);
			await Eventually.TrueAsync(() => _monitor.Intervals > before || condition(), "the keepalive loop took its turn");
			if (condition())
			{
				return ticks;
			}
		}

		Assert.Fail($"Timed out waiting until {description}.");
		return TickLimit;
	}
}
