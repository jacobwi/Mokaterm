using Microsoft.Extensions.Time.Testing;
using Mokaterm.Modules.Ssh.KeepAlive;
using Mokaterm.Modules.Ssh.Tests.Fakes;

namespace Mokaterm.Modules.Ssh.Tests.KeepAlive;

/// <summary>
/// The judgement behind keepalives that want a reply, on fake time: one keepalive per interval, a miss for every
/// interval in which the server owed a reply and sent none, and the connection lost once the misses reach the limit,
/// but only on a connection whose server has answered something. One that never answers is never dropped.
/// </summary>
public sealed class KeepAliveMonitorTests
{
	private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

	private readonly FakeTimeProvider _time = new();
	private readonly FakeKeepAliveLine _line = new();

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task AServerThatNeverAnswers_IsNeverDropped_AndIsNoLongerAsked()
	{
		await using KeepAliveMonitor monitor = Start(maxMissed: 3);

		// Nothing is owed before the first keepalive, so the first interval only sends.
		await TickAsync(monitor);
		Assert.Equal((1, 0), (_line.Sent, monitor.MissedReplies));
		await TickAsync(monitor);
		Assert.Equal((2, 1), (_line.Sent, monitor.MissedReplies));
		await TickAsync(monitor);
		Assert.Equal((3, 2), (_line.Sent, monitor.MissedReplies));

		// The third keepalive went unanswered too: this server ignores requests it does not know, and nothing it owes is
		// waited for any more, so forward requests run as they did before keepalives wanted a reply.
		await TickAsync(monitor);
		Assert.Equal(1, _line.StoppedAsking);
		Assert.Equal(new KeepAliveLineState(0, 0, false, true), _line.State);
		Assert.Equal((3, 0), (_line.Sent, monitor.MissedReplies));

		for (int round = 0; round < 10; round++)
		{
			await TickAsync(monitor);
		}

		Assert.Equal((3, 0, 1), (_line.Sent, monitor.MissedReplies, _line.StoppedAsking));
		Assert.False(monitor.Lost.IsCompleted);
	}

	[Fact]
	public async Task AServerThatAnsweredOnce_IsDroppedOnceItGoesSilent()
	{
		await using KeepAliveMonitor monitor = Start(maxMissed: 3);
		await TickAsync(monitor);
		_line.Reply();

		await TickAsync(monitor);
		Assert.Equal(0, monitor.MissedReplies);
		await TickAsync(monitor);
		await TickAsync(monitor);
		Assert.Equal(2, monitor.MissedReplies);
		Assert.False(monitor.Lost.IsCompleted);

		_time.Advance(Interval);
		KeepAliveTimeoutException lost = await monitor.Lost.WaitAsync(Eventually.Limit, Ct);

		Assert.Equal("The server stopped answering. 3 keepalives in a row went unanswered.", lost.Message);
		Assert.Equal(3, monitor.MissedReplies);
		Assert.Equal(4, _line.Sent);
		Assert.Equal(0, _line.StoppedAsking);

		// The verdict ends the loop: nothing more is sent.
		_time.Advance(Interval);
		_time.Advance(Interval);
		await Eventually.StaysTrueAsync(() => _line.Attempts == 4, "no keepalive goes out after the verdict");
	}

	[Fact]
	public async Task AReply_ResetsTheCount()
	{
		await using KeepAliveMonitor monitor = Start(maxMissed: 3);
		await TickAsync(monitor);
		_line.Reply();
		await TickAsync(monitor);
		await TickAsync(monitor);
		await TickAsync(monitor);
		Assert.Equal(2, monitor.MissedReplies);

		// A late answer to any of them, success or failure alike, says the server is still there.
		_line.Reply();
		await TickAsync(monitor);
		Assert.Equal(0, monitor.MissedReplies);

		for (int round = 0; round < 10; round++)
		{
			_line.Reply();
			await TickAsync(monitor);
			Assert.Equal(0, monitor.MissedReplies);
		}

		Assert.False(monitor.Lost.IsCompleted);

		// The count starts over from the last reply.
		await TickAsync(monitor);
		await TickAsync(monitor);
		Assert.Equal(2, monitor.MissedReplies);
		_time.Advance(Interval);
		_ = await monitor.Lost.WaitAsync(Eventually.Limit, Ct);
	}

	[Fact]
	public async Task OneMissAllowed_DropsAnAnsweringServerAfterOneSilentInterval()
	{
		await using KeepAliveMonitor monitor = Start(maxMissed: 1);
		await TickAsync(monitor);
		_line.Reply();
		await TickAsync(monitor);

		_time.Advance(Interval);
		KeepAliveTimeoutException lost = await monitor.Lost.WaitAsync(Eventually.Limit, Ct);

		Assert.Equal("The server stopped answering. 1 keepalive went unanswered.", lost.Message);
	}

	[Fact]
	public async Task ALateAnswer_AfterTheServerWasTakenNotToAnswer_IsJudgedFromThen()
	{
		await using KeepAliveMonitor monitor = Start(maxMissed: 2);
		await TickAsync(monitor);
		await TickAsync(monitor);
		await TickAsync(monitor);
		Assert.Equal(1, _line.StoppedAsking);
		await TickAsync(monitor);
		Assert.Equal(2, _line.Sent);

		// It answers after all (one of the old keepalives, or a forward): asking starts again, and now it is judged.
		_line.Reply();
		await TickAsync(monitor);
		Assert.Equal((3, 0), (_line.Sent, monitor.MissedReplies));
		await TickAsync(monitor);
		Assert.Equal(1, monitor.MissedReplies);

		_time.Advance(Interval);
		_ = await monitor.Lost.WaitAsync(Eventually.Limit, Ct);
		Assert.Equal(1, _line.StoppedAsking);
	}

	[Fact]
	public async Task ALineThatStoppedAskingOnItsOwn_IsNotAskedAndNotJudged()
	{
		// A forward start that found the server silent stops asking without the loop.
		await using KeepAliveMonitor monitor = Start(maxMissed: 1);
		await TickAsync(monitor);
		_line.StopAsking();

		for (int round = 0; round < 5; round++)
		{
			await TickAsync(monitor);
		}

		Assert.Equal((1, 0), (_line.Sent, monitor.MissedReplies));
		Assert.False(monitor.Lost.IsCompleted);
	}

	[Fact]
	public async Task WhileAForwardRequestIsOutstanding_NothingIsSent_ButItsAnswerIsOwed()
	{
		await using KeepAliveMonitor monitor = Start(maxMissed: 3);

		// A forward start is waiting for its own reply, so the line lets no keepalive out.
		_line.Owe();
		_line.Refusal = KeepAliveSend.Waiting;
		await TickAsync(monitor);
		await TickAsync(monitor);
		await TickAsync(monitor);
		Assert.Equal(0, _line.Sent);
		Assert.Equal(2, monitor.MissedReplies);

		// The forward's answer counts like any reply.
		_line.Reply();
		_line.Refusal = null;
		await TickAsync(monitor);
		Assert.Equal((1, 0), (_line.Sent, monitor.MissedReplies));
		Assert.False(monitor.Lost.IsCompleted);
	}

	[Fact]
	public async Task NothingOwed_IsNeverAMiss()
	{
		await using KeepAliveMonitor monitor = Start(maxMissed: 1);
		_line.Refusal = KeepAliveSend.Waiting;

		for (int round = 0; round < 5; round++)
		{
			await TickAsync(monitor);
		}

		Assert.Equal(0, monitor.MissedReplies);
		Assert.Equal(0, _line.StoppedAsking);
		Assert.False(monitor.Lost.IsCompleted);
	}

	[Fact]
	public async Task AHeldMessageLoop_IsNotAMiss()
	{
		FakeReceiveHold hold = new();
		await using KeepAliveMonitor monitor = Start(maxMissed: 2, hold);
		await TickAsync(monitor);
		_line.Reply();

		// The shell's flow control holds SSH.NET's message loop, so no reply can be read however alive the server is.
		hold.Hold();
		for (int round = 0; round < 5; round++)
		{
			await TickAsync(monitor);
		}

		Assert.Equal(0, monitor.MissedReplies);

		// The interval in which the hold ended does not count either: the reply may still be waiting to be read.
		hold.Release();
		await TickAsync(monitor);
		Assert.Equal(0, monitor.MissedReplies);

		await TickAsync(monitor);
		Assert.Equal(1, monitor.MissedReplies);

		// A hold that starts and ends between two looks still shows.
		hold.Hold();
		hold.Release();
		await TickAsync(monitor);
		Assert.Equal(1, monitor.MissedReplies);

		_time.Advance(Interval);
		_ = await monitor.Lost.WaitAsync(Eventually.Limit, Ct);
	}

	[Fact]
	public async Task IntervalZero_SendsNothingAndJudgesNothing()
	{
		KeepAlivePolicy off = new(TimeSpan.Zero, 3);
		Assert.False(off.IsOn);

		await using KeepAliveMonitor monitor = new(_line, off, _time);
		_time.Advance(TimeSpan.FromHours(2));

		await Eventually.StaysTrueAsync(() => _line.Attempts == 0, "nothing is sent while keepalives are off");
		Assert.Equal(0, monitor.Intervals);
		Assert.False(monitor.Lost.IsCompleted);
	}

	[Fact]
	public async Task Dispose_StopsTheLoop()
	{
		KeepAliveMonitor monitor = Start(maxMissed: 1);
		await TickAsync(monitor);
		_line.Reply();

		await monitor.DisposeAsync().AsTask().WaitAsync(Eventually.Limit, Ct);
		await monitor.DisposeAsync().AsTask().WaitAsync(Eventually.Limit, Ct);

		// Past the point the server would have been judged lost, and nothing happens.
		_time.Advance(Interval);
		_time.Advance(Interval);
		await Eventually.StaysTrueAsync(() => _line.Attempts == 1, "a disposed loop sends nothing");
		Assert.False(monitor.Lost.IsCompleted);
	}

	[Fact]
	public async Task AClosedConnection_EndsTheLoopWithoutAVerdict()
	{
		await using KeepAliveMonitor monitor = Start(maxMissed: 1);
		await TickAsync(monitor);
		_line.Reply();

		// SSH.NET reports a connection that is gone with its own reason; the keepalive says nothing over it.
		_line.Close();
		_time.Advance(Interval);
		_time.Advance(Interval);

		await Eventually.StaysTrueAsync(() => _line.Attempts == 1, "the loop stops once the connection is gone");
		Assert.False(monitor.Lost.IsCompleted);
	}

	private KeepAliveMonitor Start(int maxMissed, IReceiveHold? hold = null) =>
		new(_line, new KeepAlivePolicy(Interval, maxMissed), _time, hold);

	/// <summary>Moves one interval on and waits until the loop has looked at it.</summary>
	private async Task TickAsync(KeepAliveMonitor monitor)
	{
		long before = monitor.Intervals;
		_time.Advance(Interval);
		await Eventually.TrueAsync(() => monitor.Intervals > before, "the keepalive loop took its turn");
	}
}
