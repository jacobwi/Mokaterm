using Microsoft.Extensions.Time.Testing;
using Mokaterm.Modules.Ssh.Forwarding;
using Mokaterm.Modules.Ssh.KeepAlive;
using Mokaterm.Modules.Ssh.Tests.Fakes;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Messages.Connection;

namespace Mokaterm.Modules.Ssh.Tests.KeepAlive;

/// <summary>
/// The request line over a real SSH.NET session that never connects: replies arrive through SSH.NET's own events, the
/// way its message loop raises them, and keepalives are recorded instead of written. What matters is that a forward
/// request is never sent while any other reply is owed, and that no keepalive goes out while a forward runs.
/// </summary>
public sealed class GlobalRequestLineTests : IDisposable
{
	private static readonly TimeSpan SettleLimit = TimeSpan.FromSeconds(15);

	private readonly FakeTimeProvider _time = new();
	private readonly Session _session = SshNetSession.Create();
	private readonly RecordingMessageSender _sender = new();
	private readonly GlobalRequestLine _line;

	public GlobalRequestLineTests()
	{
		_line = new GlobalRequestLine(_session, _sender, SshNetInternals.CreateKeepAliveRequest, SettleLimit, _time);
	}

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	public void Dispose() => _line.Dispose();

	[Fact]
	public async Task TrySend_WritesOpenSshsKeepAlive_WantingAReply()
	{
		Assert.Equal(KeepAliveSend.Sent, _line.TrySend());

		GlobalRequestMessage sent = await _sender.WaitForAsync<GlobalRequestMessage>(_ => true, "the keepalive was written");
		Assert.Equal("keepalive@openssh.com", sent.RequestName);
		Assert.True(sent.WantReply);
		Assert.Equal(new KeepAliveLineState(0, 1, false), _line.State);
	}

	[Fact]
	public async Task RepliesOfEitherKind_AreCounted_AgainstWhatIsOwed()
	{
		await SendAndWaitAsync();
		await SendAndWaitAsync();

		_session.Receive(new RequestFailureMessage());
		Assert.Equal(new KeepAliveLineState(1, 1, false), _line.State);
		_session.Receive(new RequestSuccessMessage());
		Assert.Equal(new KeepAliveLineState(2, 0, false), _line.State);

		// A reply nobody here asked for still says the server is there, and owes nothing back.
		_session.Receive(new RequestSuccessMessage(8080));
		Assert.Equal(new KeepAliveLineState(3, 0, false), _line.State);
	}

	[Fact]
	public async Task NoKeepAliveGoesOut_WhileAForwardStartRuns()
	{
		using ManualResetEventSlim running = new();
		using ManualResetEventSlim release = new();
		Task start = _line.StartAsync(
			() =>
			{
				running.Set();
				Assert.True(release.Wait(Eventually.Limit, Ct), "The test let the forward finish.");
			},
			Ct);
		Assert.True(running.Wait(Eventually.Limit, Ct), "The forward started.");

		Assert.Equal(KeepAliveSend.Waiting, _line.TrySend());
		Assert.Equal(KeepAliveSend.Waiting, _line.TrySend());
		Assert.Empty(_sender.Sent);

		release.Set();
		await start.WaitAsync(Eventually.Limit, Ct);

		// The forward's own request is owed until its reply, and keepalives go out again beside it.
		Assert.Equal(1, _line.State.Owed);
		Assert.Equal(KeepAliveSend.Sent, _line.TrySend());
		_ = await _sender.WaitForAsync<GlobalRequestMessage>(_ => true, "a keepalive went out after the forward");
	}

	[Fact]
	public async Task AForwardStart_WaitsUntilEveryEarlierRequestIsAnswered()
	{
		await SendAndWaitAsync();
		using ManualResetEventSlim started = new();
		Task start = _line.StartAsync(started.Set, Ct);

		// The keepalive's reply has not come, so it could still arrive after the forward asked and pass for its answer.
		await Eventually.StaysTrueAsync(() => !started.IsSet, "the forward waits for the keepalive's reply");
		Assert.Equal(KeepAliveSend.Waiting, _line.TrySend());

		_session.Receive(new RequestFailureMessage());
		await start.WaitAsync(Eventually.Limit, Ct);
		Assert.True(started.IsSet);
		Assert.Equal(1, _line.State.Owed);
	}

	[Fact]
	public async Task AForwardStop_CountsItsCancel_SoTheNextStartWaitsForThatAnswer()
	{
		// SSH.NET's stop can return before the answer to its cancel arrived, because any earlier reply set its wait handle.
		await _line.StopAsync(() => { }, sendsRequest: true).WaitAsync(Eventually.Limit, Ct);
		Assert.Equal(1, _line.State.Owed);

		using ManualResetEventSlim started = new();
		Task start = _line.StartAsync(started.Set, Ct);
		await Eventually.StaysTrueAsync(() => !started.IsSet, "the next forward waits for the cancel's answer");

		_session.Receive(new RequestSuccessMessage());
		await start.WaitAsync(Eventually.Limit, Ct);
		Assert.True(started.IsSet);
	}

	[Fact]
	public async Task AStopThatSendsNothing_OwesNothing()
	{
		bool stopped = false;

		await _line.StopAsync(() => stopped = true, sendsRequest: false).WaitAsync(Eventually.Limit, Ct);

		Assert.True(stopped);
		Assert.Equal(0, _line.State.Owed);
	}

	[Fact]
	public async Task OnAServerThatAnswered_AForwardStartGivesUp_WhenAnEarlierRequestStaysUnanswered()
	{
		_session.Receive(new RequestSuccessMessage());
		await SendAndWaitAsync();
		bool started = false;
		Task start = _line.StartAsync(() => started = true, Ct);

		// It answered before, so a silence past the connect timeout means slow or gone, not a server that ignores requests.
		_time.Advance(SettleLimit - TimeSpan.FromSeconds(1));
		await Eventually.StaysTrueAsync(() => !start.IsCompleted, "the start waits up to the settle limit");
		await Eventually.AdvanceUntilAsync(_time, TimeSpan.FromSeconds(1), () => start.IsCompleted, "the forward start gave up");

		TimeoutException timeout = await Assert.ThrowsAsync<TimeoutException>(() => start);
		Assert.Equal("The server has not answered an earlier request yet. Try again in a moment.", timeout.Message);
		Assert.False(started);
		Assert.False(_line.State.Ignored);

		// Giving up lets keepalives out again; the one still owed keeps counting.
		Assert.Equal(1, _line.State.Owed);
		await SendAndWaitAsync();
		Assert.Equal(2, _line.State.Owed);
	}

	[Fact]
	public async Task OnAServerThatNeverAnswered_AStartWaitsBriefly_ThenTheLineStopsAsking()
	{
		await SendAndWaitAsync();
		using ManualResetEventSlim started = new();
		Task start = _line.StartAsync(started.Set, Ct);

		// A server that answers does so within a round trip, so the wait is short.
		_time.Advance(GlobalRequestLine.UnprovenWait - TimeSpan.FromMilliseconds(100));
		await Eventually.StaysTrueAsync(() => !started.IsSet, "the start waits for the keepalive's reply a little");
		_time.Advance(TimeSpan.FromMilliseconds(200));
		await start.WaitAsync(Eventually.Limit, Ct);

		// From here on the server is taken not to answer: nothing is asked and nothing is owed, not even the forward.
		Assert.True(started.IsSet);
		Assert.Equal(new KeepAliveLineState(0, 0, false, true), _line.State);
		Assert.True(_line.StoppedAsking.IsCompleted);
		Assert.Equal(KeepAliveSend.Waiting, _line.TrySend());
		Assert.Single(_sender.Sent);
	}

	[Fact]
	public async Task AfterTheLineStoppedAsking_ForwardsRunAsBefore_UntilTheServerAnswersAnything()
	{
		await SendAndWaitAsync();
		_line.StopAsking();
		Assert.Equal(new KeepAliveLineState(0, 0, false, true), _line.State);

		// No time passes: nothing holds a forward, and nothing it sends is counted.
		using ManualResetEventSlim started = new();
		await _line.StartAsync(started.Set, Ct).WaitAsync(Eventually.Limit, Ct);
		await _line.StopAsync(() => { }, sendsRequest: true).WaitAsync(Eventually.Limit, Ct);
		Assert.True(started.IsSet);
		Assert.Equal(0, _line.State.Owed);

		// A late answer, to the old keepalive or to the forward, starts the counting over.
		_session.Receive(new RequestFailureMessage());
		Assert.Equal(new KeepAliveLineState(1, 0, false, false), _line.State);
		await SendAndWaitAsync();
		Assert.Equal(1, _line.State.Owed);
	}

	[Fact]
	public async Task OnAServerThatNeverAnswered_RepliesHeldUpByTheShell_MakeTheStartGiveUpInstead()
	{
		FakeReceiveHold hold = new();
		using GlobalRequestLine line = new(_session, _sender, SshNetInternals.CreateKeepAliveRequest, SettleLimit, _time, hold);
		Assert.Equal(KeepAliveSend.Sent, line.TrySend());
		await _sender.WaitForAsync<GlobalRequestMessage>(_ => true, "the keepalive was written");

		// Our own flow control held the message loop, so its reply may be sitting there unread.
		hold.Hold();
		Task start = line.StartAsync(() => { }, Ct);
		await Eventually.AdvanceUntilAsync(_time, TimeSpan.FromSeconds(1), () => start.IsCompleted, "the forward start gave up");

		_ = await Assert.ThrowsAsync<TimeoutException>(() => start);
		Assert.False(line.State.Ignored);
		Assert.Equal(1, line.State.Owed);
	}

	[Fact]
	public async Task AForwardRefusedBeforeItWasSent_LeavesNothingOwed()
	{
		await Assert.ThrowsAsync<InvalidOperationException>(
			() => _line.StartAsync(() => throw new InvalidOperationException("Forwarded port is not added to a client."), Ct));
		await Assert.ThrowsAsync<SshConnectionException>(
			() => _line.StartAsync(() => throw new SshConnectionException("Client not connected."), Ct));

		Assert.Equal(0, _line.State.Owed);
	}

	[Fact]
	public async Task AForwardThatFailsAfterSending_KeepsItsAnswerOwed()
	{
		// SSH.NET gave up waiting for the reply, which may still come and must not pass for the next request's.
		await Assert.ThrowsAsync<SshOperationTimeoutException>(
			() => _line.StartAsync(() => throw new SshOperationTimeoutException("Operation has timed out."), Ct));

		Assert.Equal(1, _line.State.Owed);
	}

	[Fact]
	public async Task AFailedWrite_TakesItsCountBack_AndClosesTheLineOfASessionThatIsGone()
	{
		_sender.Connected = false;

		Assert.Equal(KeepAliveSend.Sent, _line.TrySend());

		await Eventually.TrueAsync(() => _line.State.Closed, "the line saw the session is gone");
		Assert.Equal(0, _line.State.Owed);
		Assert.Equal(KeepAliveSend.Closed, _line.TrySend());
	}

	[Fact]
	public async Task Dispose_WakesAWaitingStart_StopsCounting_AndStillRunsStops()
	{
		await SendAndWaitAsync();
		Task start = _line.StartAsync(() => { }, Ct);
		Assert.False(start.IsCompleted);

		_line.Dispose();

		_ = await Assert.ThrowsAsync<SshConnectionException>(() => start.WaitAsync(Eventually.Limit, Ct));
		_session.Receive(new RequestSuccessMessage());
		Assert.Equal(0, _line.State.Replies);
		Assert.True(_line.State.Closed);

		// A tunnel closing with the session still stops its port, so the server stops listening.
		bool stopped = false;
		await _line.StopAsync(() => stopped = true, sendsRequest: true).WaitAsync(Eventually.Limit, Ct);
		Assert.True(stopped);
	}

	/// <summary>Sends one keepalive once the last one is written, which happens on the pool, and waits until it is.</summary>
	private async Task SendAndWaitAsync()
	{
		int before = _sender.Sent.Count;
		await Eventually.TrueAsync(() => _line.TrySend() == KeepAliveSend.Sent, "the line let a keepalive out");
		await Eventually.TrueAsync(() => _sender.Sent.Count > before, "the keepalive was written");
	}
}
