using Microsoft.Extensions.Time.Testing;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Modules.Ssh.Admin;
using Mokaterm.Modules.Ssh.Admin.Detection;
using Mokaterm.Modules.Ssh.Admin.Reboot;
using Mokaterm.Modules.Ssh.Tests.Fakes;

namespace Mokaterm.Modules.Ssh.Tests.Admin;

/// <summary>Whether a reboot is due, the reboot itself, and reconnecting the session once the machine is back.</summary>
public sealed class RebootTests
{
	private static readonly TimeSpan Step = TimeSpan.FromSeconds(5);

	private static readonly RebootWatchTimes Times = new(
		Down: TimeSpan.FromMinutes(1),
		Back: TimeSpan.FromMinutes(3),
		FirstWait: TimeSpan.FromSeconds(10),
		Retry: TimeSpan.FromSeconds(10),
		AttemptStart: TimeSpan.FromSeconds(2));

	private readonly FakeTimeProvider _time = new();

	[Fact]
	public void Status_TheDebianFlag_NamesThePackagesThatAskedForIt()
	{
		RebootStatus status = RebootCommands.Parse("@@mt:flag\n*** System restart required ***\n@@mt:packages\nlibc6\nlinux-image-6.8.0-47-generic\n@@mt:flag-watch\n");

		Assert.True(status.Required);
		Assert.Equal(["libc6", "linux-image-6.8.0-47-generic"], status.Packages);
		Assert.Equal("/var/run/reboot-required", status.Source);
	}

	[Fact]
	public void Status_NeedsRestarting_Required()
	{
		const string Output = """
			@@mt:needs-restarting
			Core libraries or services have been updated since boot-up:
			  * kernel
			  * systemd

			Reboot is required to fully utilize these updates.
			More information: https://access.redhat.com/solutions/27943
			@@mt:needs-restarting-exit
			1
			""";

		RebootStatus status = RebootCommands.Parse(Output.ReplaceLineEndings("\n"));

		Assert.True(status.Required);
		Assert.Equal(["kernel", "systemd"], status.Packages);
		Assert.Equal("needs-restarting", status.Source);
	}

	[Fact]
	public void Status_NeedsRestarting_NotNeeded()
	{
		RebootStatus status = RebootCommands.Parse("@@mt:needs-restarting\nNo core libraries or services have been updated since boot-up.\nReboot should not be necessary.\n@@mt:needs-restarting-exit\n0\n");

		Assert.False(status.Required);
	}

	[Fact]
	public void Status_AKernelWhoseModulesAreGone_WantsAReboot() =>
		Assert.True(RebootCommands.Parse("@@mt:modules-gone\n").Required);

	[Fact]
	public void Status_WithTheFlagHooksAndNoFlag_IsNotNeeded_ButWithoutThemIsUnknown()
	{
		Assert.False(RebootCommands.Parse("@@mt:flag-watch\n").Required);
		Assert.Null(RebootCommands.Parse("").Required);
	}

	[Fact]
	public void Status_ReadsWithoutRoot_AndParsesInSh() =>
		Assert.False(RebootCommands.Status().AsRoot);

	[Theory]
	[InlineData(nameof(InitSystem.Systemd), "systemctl reboot")]
	[InlineData(nameof(InitSystem.SysV), "shutdown -r now")]
	[InlineData(nameof(InitSystem.OpenRc), "reboot")]
	[InlineData(nameof(InitSystem.BusyBox), "reboot")]
	public void Reboot_IsTheInitSystemsOwn_AndSaysItCutsTheSession(string init, string expected)
	{
		AdminAction action = RebootCommands.Reboot(Profile(Enum.Parse<InitSystem>(init)));

		Assert.Equal(expected, action.Script.Body);
		Assert.True(action.Script.AsRoot);
		Assert.True(action.CutsConnection);
		Assert.True(action.Destructive);
		Assert.Equal("Reboot web1", action.Title);
	}

	[Fact]
	public void Reboot_IsNotOffered_WithoutAnInitSystem()
	{
		SystemProfile container = Profile(InitSystem.None);

		Assert.False(RebootCommands.CanReboot(container));
		Assert.Throws<InvalidOperationException>(() => RebootCommands.Reboot(container));
	}

	[Fact]
	public async Task Watch_ReconnectsOnce_TheServerAnswersAgain()
	{
		ScriptedSessionHandle session = new();
		bool answers = false;
		int reconnects = 0;
		RebootWatch watch = new(
			session,
			_ =>
			{
				reconnects++;
				session.Move(SessionState.Connecting);
				session.Move(SessionState.Connected);
				return Task.CompletedTask;
			},
			_ => Task.FromResult(Volatile.Read(ref answers)),
			() => true,
			_time,
			Times);

		Task run = watch.RunAsync(TestContext.Current.CancellationToken);
		Assert.Equal(RebootWatchState.WaitingForDown, watch.State);

		session.Move(SessionState.Failed, ConnectFailure.HostUnreachable, "The server stopped responding.");
		await Eventually.TrueAsync(() => watch.State == RebootWatchState.WaitingForServer, "the drop is noticed");
		await Eventually.AdvanceUntilAsync(_time, Step, () => _time.GetUtcNow() - watch.StartedAt > TimeSpan.FromSeconds(40), "a few rounds go by");
		Assert.Equal(0, reconnects);

		Volatile.Write(ref answers, true);
		await Eventually.AdvanceUntilAsync(_time, Step, () => watch.State == RebootWatchState.Back, "the session is back");
		await run.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

		Assert.Equal(1, reconnects);
		Assert.Equal(1, watch.Attempts);
	}

	[Fact]
	public async Task Watch_GivesUp_WhenTheMachineNeverWentDown()
	{
		ScriptedSessionHandle session = new();
		RebootWatch watch = new(session, _ => Task.CompletedTask, null, () => true, _time, Times);

		Task run = watch.RunAsync(TestContext.Current.CancellationToken);
		await Eventually.AdvanceUntilAsync(_time, Step, () => watch.State == RebootWatchState.GaveUp, "it gives up");
		await run.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

		Assert.Contains("still up", watch.Problem, StringComparison.Ordinal);
		Assert.Equal(0, watch.Attempts);
	}

	[Fact]
	public async Task Watch_GivesUp_WhenTheServerNeverAnswers()
	{
		ScriptedSessionHandle session = new();
		int reconnects = 0;
		RebootWatch watch = new(session, _ => { reconnects++; return Task.CompletedTask; }, _ => Task.FromResult(false), () => true, _time, Times);

		Task run = watch.RunAsync(TestContext.Current.CancellationToken);
		session.Move(SessionState.Disconnected);
		await Eventually.AdvanceUntilAsync(_time, Step, () => watch.State == RebootWatchState.GaveUp, "it gives up", maxSteps: 120);
		await run.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

		Assert.Equal(0, reconnects);
		Assert.Contains("did not answer", watch.Problem, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Watch_BehindAJumpHost_ReconnectsEveryRound_UntilOneConnects()
	{
		ScriptedSessionHandle session = new();
		int reconnects = 0;
		RebootWatch watch = new(
			session,
			_ =>
			{
				int attempt = Interlocked.Increment(ref reconnects);
				session.Move(SessionState.Connecting);
				session.Move(attempt < 3 ? SessionState.Failed : SessionState.Connected, attempt < 3 ? ConnectFailure.HostUnreachable : null);
				return Task.CompletedTask;
			},
			answers: null,
			() => true,
			_time,
			Times);

		Task run = watch.RunAsync(TestContext.Current.CancellationToken);
		session.Move(SessionState.Failed, ConnectFailure.HostUnreachable);
		await Eventually.AdvanceUntilAsync(_time, Step, () => watch.State == RebootWatchState.Back, "the third try connects");
		await run.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

		Assert.Equal(3, watch.Attempts);
	}

	[Fact]
	public async Task Watch_StopsAtALoginThatNeedsTheUser()
	{
		ScriptedSessionHandle session = new();
		RebootWatch watch = new(
			session,
			_ =>
			{
				session.Move(SessionState.Connecting);
				session.Move(SessionState.Failed, ConnectFailure.AuthenticationFailed, "Permission denied (publickey).");
				return Task.CompletedTask;
			},
			_ => Task.FromResult(true),
			() => true,
			_time,
			Times);

		Task run = watch.RunAsync(TestContext.Current.CancellationToken);
		session.Move(SessionState.Disconnected);
		await Eventually.AdvanceUntilAsync(_time, Step, () => watch.State == RebootWatchState.GaveUp, "it stops");
		await run.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

		Assert.Equal(1, watch.Attempts);
		Assert.Contains("Permission denied", watch.Problem, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Watch_Stops_WhenTheTabCloses_OrTheUserStopsIt()
	{
		ScriptedSessionHandle closing = new();
		bool open = true;
		RebootWatch closed = new(closing, _ => Task.CompletedTask, _ => Task.FromResult(false), () => Volatile.Read(ref open), _time, Times);
		Task first = closed.RunAsync(TestContext.Current.CancellationToken);
		closing.Move(SessionState.Disconnected);
		Volatile.Write(ref open, false);
		await Eventually.AdvanceUntilAsync(_time, Step, () => closed.State == RebootWatchState.Stopped, "a closed tab stops it");
		await first.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

		using CancellationTokenSource stop = new();
		RebootWatch stopped = new(new ScriptedSessionHandle(), _ => Task.CompletedTask, null, () => true, _time, Times);
		Task second = stopped.RunAsync(stop.Token);
		await stop.CancelAsync();
		await second.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

		Assert.Equal(RebootWatchState.Stopped, stopped.State);
	}

	private static SystemProfile Profile(InitSystem init) => SystemProfile.From(SystemProbe.Parse(DetectionFixtures.Debian12)) with { Init = init };
}
