using Microsoft.Extensions.Time.Testing;
using Mokaterm.UI.Common.ToolPages;

namespace Mokaterm.UI.Tests;

/// <summary>The polling every tool page shares: ticks, skips, replacing and stopping.</summary>
public sealed class PagePollerTests
{
	private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);

	private readonly FakeTimeProvider _time = new();

	[Fact]
	public async Task Start_TicksOnEveryInterval()
	{
		int ticks = 0;
		using PagePoller poller = Poller(() => true);

		poller.Start(Interval, () => Tick(ref ticks), TestContext.Current.CancellationToken);
		await Eventually.AdvanceUntilAsync(_time, Interval, () => Volatile.Read(ref ticks) >= 3, "three ticks");

		Assert.True(poller.IsRunning);
	}

	[Fact]
	public async Task ATickThePageRefuses_IsSkipped_AndNotMadeUpLater()
	{
		int ticks = 0;
		bool allowed = false;
		using PagePoller poller = Poller(() => Volatile.Read(ref allowed));

		poller.Start(Interval, () => Tick(ref ticks), TestContext.Current.CancellationToken);
		for (int i = 0; i < 5; i++)
		{
			_time.Advance(Interval);
			await Task.Delay(20, TestContext.Current.CancellationToken);
		}

		Assert.Equal(0, Volatile.Read(ref ticks));

		Volatile.Write(ref allowed, true);
		await Eventually.AdvanceUntilAsync(_time, Interval, () => Volatile.Read(ref ticks) >= 1, "a tick once allowed");
		Assert.Equal(1, Volatile.Read(ref ticks));
	}

	[Fact]
	public async Task StartingAgain_ReplacesThePoll()
	{
		int first = 0;
		int second = 0;
		using PagePoller poller = Poller(() => true);

		poller.Start(Interval, () => Tick(ref first), TestContext.Current.CancellationToken);
		poller.Start(Interval, () => Tick(ref second), TestContext.Current.CancellationToken);
		await Eventually.AdvanceUntilAsync(_time, Interval, () => Volatile.Read(ref second) >= 2, "the second poll ticks");

		Assert.Equal(0, Volatile.Read(ref first));
	}

	[Fact]
	public async Task StopAndDispose_EndTheTicks()
	{
		int ticks = 0;
		PagePoller poller = Poller(() => true);
		poller.Start(Interval, () => Tick(ref ticks), TestContext.Current.CancellationToken);
		await Eventually.AdvanceUntilAsync(_time, Interval, () => Volatile.Read(ref ticks) >= 1, "a first tick");

		poller.Stop();
		int afterStop = Volatile.Read(ref ticks);
		_time.Advance(Interval * 3);
		await Task.Delay(50, TestContext.Current.CancellationToken);

		Assert.False(poller.IsRunning);
		Assert.Equal(afterStop, Volatile.Read(ref ticks));

		poller.Dispose();
		poller.Start(Interval, () => Tick(ref ticks), TestContext.Current.CancellationToken);
		Assert.False(poller.IsRunning);
	}

	[Fact]
	public void Start_WithAnEndedLifetime_DoesNothing()
	{
		using CancellationTokenSource ended = new();
		ended.Cancel();
		using PagePoller poller = Poller(() => true);

		poller.Start(Interval, () => Task.CompletedTask, ended.Token);

		Assert.False(poller.IsRunning);
	}

	private PagePoller Poller(Func<bool> mayTick) => new(_time, tick => tick(), mayTick);

	private static Task Tick(ref int ticks)
	{
		_ = Interlocked.Increment(ref ticks);
		return Task.CompletedTask;
	}
}
