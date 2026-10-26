using Microsoft.Extensions.Time.Testing;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.Modules.Ssh.Tools;

namespace Mokaterm.Modules.Ssh.Tests.Docker;

public sealed class DockerRestartLoopTests
{
	private static readonly DateTimeOffset Start = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

	private readonly FakeTimeProvider _time = new(Start);
	private readonly DockerRestartLoops _loops = new();
	private readonly DockerChangeLog _changes;

	public DockerRestartLoopTests() => _changes = new DockerChangeLog(_time);

	[Fact]
	public void RoseTooOften_NeedsThreeRestarts_WithinTheWindow()
	{
		Assert.Equal(3, DockerRestartLoop.Restarts);
		Assert.Equal(TimeSpan.FromMinutes(10), DockerRestartLoop.Window);

		Assert.False(DockerRestartLoop.RoseTooOften([Sample(0, 4)]));
		Assert.False(DockerRestartLoop.RoseTooOften([Sample(0, 4), Sample(5, 6)]));
		Assert.True(DockerRestartLoop.RoseTooOften([Sample(0, 4), Sample(5, 6), Sample(9, 7)]));
		Assert.True(DockerRestartLoop.RoseTooOften([Sample(0, 4), Sample(10, 7)]));
		Assert.False(DockerRestartLoop.RoseTooOften([Sample(0, 4), Sample(1, 5), Sample(11, 7)]));
	}

	[Fact]
	public void RoseTooOften_AfterALongPause_NeedsTheCountForEveryWindowOfIt()
	{
		Assert.False(DockerRestartLoop.RoseTooOften([Sample(0, 0), Sample(25, 8)]));
		Assert.True(DockerRestartLoop.RoseTooOften([Sample(0, 0), Sample(25, 9)]));
		Assert.Equal((9, TimeSpan.FromMinutes(25)), DockerRestartLoop.RecentRestarts([Sample(0, 0), Sample(25, 9)]));
	}

	[Fact]
	public void StableAfter_IsTwoMinutes_OrTwiceTheLongestRun()
	{
		Assert.Equal(TimeSpan.FromMinutes(2), DockerRestartLoop.StableAfter([]));
		Assert.Equal(TimeSpan.FromMinutes(2), DockerRestartLoop.StableAfter([TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(50)]));
		Assert.Equal(TimeSpan.FromMinutes(6), DockerRestartLoop.StableAfter([TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(3)]));
	}

	[Fact]
	public void Restarting_IsALoopAtOnce_EvenOnTheFirstRead_AndIsToldOnce()
	{
		DockerRestartRecord loop = Assert.Single(Observe(State("worker", "restarting", restarts: 5, exit: 1)));

		Assert.Equal(DockerRestartLoopReason.Restarting, loop.Reason);
		Assert.True(loop.IsLooping);
		Assert.True(_loops.IsLooping("id-worker"));
		Assert.Empty(Observe(State("worker", "restarting", restarts: 6, exit: 1)));
		Assert.Equal("worker", Assert.Single(_loops.Looping).Name);
	}

	[Fact]
	public void Restarting_UnderAPolicyOfNo_IsNotALoop()
	{
		Assert.Empty(Observe(State("worker", "restarting", restarts: 5, policy: "no")));
		Assert.Empty(Observe(State("worker", "restarting", restarts: 5, policy: "")));
		Assert.Empty(_loops.Looping);
	}

	[Fact]
	public void ThreeRestartsWithinTenMinutes_IsALoop_TwoAreNot()
	{
		Observe(State("worker", restarts: 0));
		_time.Advance(TimeSpan.FromMinutes(2));
		Assert.Empty(Observe(State("worker", restarts: 1)));
		_time.Advance(TimeSpan.FromMinutes(2));
		Assert.Empty(Observe(State("worker", restarts: 2)));
		_time.Advance(TimeSpan.FromMinutes(2));

		DockerRestartRecord loop = Assert.Single(Observe(State("worker", restarts: 3)));

		Assert.Equal(DockerRestartLoopReason.RoseTooOften, loop.Reason);
		Assert.Equal(3, loop.RecentRestarts);
		Assert.InRange(loop.RecentSpan, TimeSpan.FromMinutes(6), TimeSpan.FromMinutes(6.1));
	}

	[Fact]
	public void RestartsSlowerThanTheWindowAllows_AreNotALoop()
	{
		for (int restarts = 0; restarts < 10; restarts++)
		{
			Assert.Empty(Observe(State("worker", restarts: restarts)));
			_time.Advance(TimeSpan.FromMinutes(4));
		}

		Assert.Empty(_loops.Looping);
	}

	[Fact]
	public void AStableRun_EndsTheLoop_AndSaysSo()
	{
		int changed = 0;
		_loops.Changed += () => changed++;
		Observe(State("worker", "restarting", restarts: 5));
		Assert.Equal(1, changed);

		_time.Advance(TimeSpan.FromSeconds(1));
		Observe(State("worker", restarts: 6));
		_time.Advance(TimeSpan.FromMinutes(1));
		Observe(State("worker", restarts: 6));
		Assert.True(_loops.IsLooping("id-worker"));

		_time.Advance(TimeSpan.FromMinutes(1));
		Observe(State("worker", restarts: 6));

		Assert.False(_loops.IsLooping("id-worker"));
		Assert.Empty(_loops.Looping);
		Assert.Equal(3, changed);
	}

	[Fact]
	public void AContainerThatDiesEveryFewMinutes_HasToOutlastItsRuns()
	{
		DateTimeOffset started = Start.AddMinutes(-4);
		Observe(State("worker", "restarting", restarts: 5, started: started, finished: started.AddMinutes(3)));
		_time.Advance(TimeSpan.FromSeconds(1));
		DateTimeOffset again = started.AddMinutes(3).AddSeconds(5);
		Observe(State("worker", restarts: 6, started: again, finished: started.AddMinutes(3)));

		_time.Advance(TimeSpan.FromMinutes(3));
		Observe(State("worker", restarts: 6, started: again, finished: started.AddMinutes(3)));
		Assert.True(_loops.IsLooping("id-worker"));

		_time.Advance(TimeSpan.FromMinutes(3));
		Observe(State("worker", restarts: 6, started: again, finished: started.AddMinutes(3)));
		Assert.False(_loops.IsLooping("id-worker"));
	}

	[Fact]
	public void TheUsersOwnRestarts_NeverAddUpToALoop()
	{
		Observe(State("worker", restarts: 0));
		_time.Advance(TimeSpan.FromMinutes(1));
		using (_changes.Begin([DockerCommands.Restart("worker")]))
		{
			Assert.Empty(Observe(State("worker", restarts: 2)));
		}

		_time.Advance(TimeSpan.FromMinutes(1));
		Assert.Empty(Observe(State("worker", restarts: 3)));
		Assert.Empty(_loops.Looping);
	}

	[Fact]
	public void WithoutTheUsersChange_TheSameRestartsAreALoop()
	{
		Observe(State("worker", restarts: 0));
		_time.Advance(TimeSpan.FromMinutes(1));
		Assert.Empty(Observe(State("worker", restarts: 2)));
		_time.Advance(TimeSpan.FromMinutes(1));

		Assert.Single(Observe(State("worker", restarts: 3)));
	}

	[Fact]
	public void TheUsersStop_EndsTheLoop()
	{
		Observe(State("worker", "restarting", restarts: 5));
		_time.Advance(TimeSpan.FromSeconds(10));
		using (_changes.Begin([DockerCommands.Stop("worker")]))
		{
			Observe(State("worker", "exited", restarts: 5, exit: 0));
		}

		Assert.Empty(_loops.Looping);
	}

	[Fact]
	public void StoppingForGood_EndsTheLoop()
	{
		Observe(State("worker", "restarting", restarts: 9, policy: "on-failure:10"));
		_time.Advance(TimeSpan.FromSeconds(10));
		Observe(State("worker", "exited", restarts: 10, exit: 1, policy: "on-failure:10", finished: Start.AddSeconds(5)));

		Assert.False(_loops.IsLooping("id-worker"));
		Assert.Equal(1, _loops.Find("id-worker")!.LastExit!.ExitCode);
	}

	[Fact]
	public void ADockerStartFromElsewhere_StartsTheCountOver()
	{
		Observe(State("worker", restarts: 7));
		_time.Advance(TimeSpan.FromMinutes(1));
		Observe(State("worker", restarts: 0));
		_time.Advance(TimeSpan.FromMinutes(1));

		Assert.Empty(Observe(State("worker", restarts: 2)));
	}

	[Fact]
	public void AContainerThatWentAway_IsForgotten()
	{
		int changed = 0;
		_loops.Changed += () => changed++;
		Observe(State("worker", "restarting", restarts: 5), State("web"));

		Observe(State("web"));

		Assert.Null(_loops.Find("id-worker"));
		Assert.NotNull(_loops.Find("id-web"));
		Assert.Equal(2, changed);
	}

	[Fact]
	public void TheLastExit_OutlivesTheNextStart()
	{
		DateTimeOffset finished = Start.AddSeconds(-3);
		Observe(State("worker", "restarting", restarts: 5, exit: 137, oom: true, started: finished.AddSeconds(-6), finished: finished));
		_time.Advance(TimeSpan.FromSeconds(10));
		Observe(State("worker", restarts: 6, exit: 0, started: Start.AddSeconds(5), finished: finished));

		DockerContainerExit exit = _loops.Find("id-worker")!.LastExit!;
		Assert.Equal(137, exit.ExitCode);
		Assert.True(exit.OomKilled);
		Assert.Equal(finished, exit.FinishedAt);
	}

	[Fact]
	public void RunLengths_AreTakenOnlyWhereDockerPairsAStartWithItsEnd()
	{
		DateTimeOffset first = Start.AddSeconds(-20);
		Observe(State("worker", "restarting", restarts: 5, started: first, finished: first.AddSeconds(6)));
		_time.Advance(TimeSpan.FromSeconds(10));
		DateTimeOffset second = first.AddSeconds(11);
		Observe(State("worker", restarts: 6, started: second, finished: first.AddSeconds(6)));
		_time.Advance(TimeSpan.FromSeconds(10));
		Observe(State("worker", restarts: 7, started: second.AddSeconds(9), finished: second.AddSeconds(5)));
		_time.Advance(TimeSpan.FromSeconds(10));
		Observe(State("worker", restarts: 9, started: second.AddSeconds(30), finished: second.AddSeconds(25)));

		Assert.Equal([TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(6)], _loops.Find("id-worker")!.RecentRuns);
	}

	[Fact]
	public void FindLooping_GoesByIdPrefixOrName()
	{
		const string Id = "0a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f9";
		Observe(State("worker", "restarting", restarts: 5) with { Id = Id }, State("web"));

		Assert.NotNull(_loops.FindLooping(Id));
		Assert.NotNull(_loops.FindLooping(Id[..12]));
		Assert.NotNull(_loops.FindLooping("", "worker"));
		Assert.Null(_loops.FindLooping(Id[..8]));
		Assert.Null(_loops.FindLooping("id-web", "web"));
	}

	[Theory]
	[InlineData(137, false, "137, killed (SIGKILL): out of memory, or killed from outside")]
	[InlineData(137, true, "137, killed when it ran out of memory")]
	[InlineData(139, false, "139, segmentation fault (SIGSEGV): the program crashed")]
	[InlineData(143, false, "143, terminated (SIGTERM): it was asked to stop")]
	[InlineData(1, false, "1, general error: the program failed")]
	[InlineData(125, false, "125, docker itself could not run the container")]
	[InlineData(126, false, "126, the command could not run: not executable, or no permission")]
	[InlineData(127, false, "127, the command was not found in the image")]
	[InlineData(131, false, "131, ended by signal 3")]
	[InlineData(0, false, "0, it ended without an error")]
	[InlineData(42, false, "42, the program's own error code")]
	public void ExitCodes_SayWhatTheyUsuallyMean(int code, bool oomKilled, string expected) =>
		Assert.Equal(expected, DockerExitCodes.Describe(code, oomKilled));

	[Fact]
	public void Wording_SaysWhyAndHowItLastEnded()
	{
		DockerRestartRecord counted = new()
		{
			Id = "id-worker",
			Name = "worker",
			IsLooping = true,
			Reason = DockerRestartLoopReason.RoseTooOften,
			RestartCount = 9,
			RecentRestarts = 4,
			RecentSpan = TimeSpan.FromMinutes(2.5),
			LastExit = new DockerContainerExit(137, true, "", Start),
		};
		DockerRestartRecord restarting = counted with { Reason = DockerRestartLoopReason.Restarting, LastExit = null };

		Assert.Equal(
			"worker is in a restart loop: Docker restarted it 4 times in 3 minutes. Its last exit was 137, killed when it ran out of memory.",
			DockerRestartWording.Notice(counted));
		Assert.Equal("It stopped again and Docker is about to restart it (9 times so far).", DockerRestartWording.Summary(restarting));
		Assert.Equal("1 container keeps restarting", DockerRestartWording.Count(1));
		Assert.Equal("2 containers keep restarting", DockerRestartWording.Count(2));
		Assert.Equal("on-failure:5: Docker starts it again after an error, 5 times at most", DockerRestartWording.Policy("on-failure:5"));
		Assert.Equal("no: Docker does not start it again", DockerRestartWording.Policy(""));
		Assert.Equal("6 s, 5 s, newest first", DockerRestartWording.Runs([TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(5)]));
		Assert.Equal("in under a minute", DockerRestartWording.Within(TimeSpan.FromSeconds(40)));
	}

	[Fact]
	public void BeforeRun_KeepsTheLinesFromBeforeTheContainerStartedAgain()
	{
		LogLine Line(string time, string text) => new(time, text, IsError: false);
		IReadOnlyList<LogLine> lines =
		[
			Line("2026-10-20T08:59:50.100000000Z", "starting"),
			Line("2026-10-20T08:59:55.900000000Z", "panic: out of cheese"),
			Line("2026-10-20T09:00:01.000000001Z", "starting"),
		];

		Assert.Equal(["starting", "panic: out of cheese"], DockerRestartLoop.BeforeRun(lines, Start)!.Select(line => line.Text));
		Assert.Same(lines, DockerRestartLoop.BeforeRun(lines, Start.AddMinutes(1)));
		Assert.Null(DockerRestartLoop.BeforeRun(lines, Start.AddMinutes(-1)));
	}

	private static DockerRestartSample Sample(double minutes, int count) => new(Start.AddMinutes(minutes), count);

	private static DockerContainerState State(
		string name,
		string state = "running",
		int restarts = 0,
		int exit = 0,
		string policy = "always",
		bool oom = false,
		DateTimeOffset? started = null,
		DateTimeOffset? finished = null) => new()
		{
			Id = "id-" + name,
			Name = name,
			State = state,
			RestartCount = restarts,
			ExitCode = exit,
			RestartPolicy = policy,
			OomKilled = oom,
			StartedAt = started,
			FinishedAt = finished,
		};

	private IReadOnlyList<DockerRestartRecord> Observe(params DockerContainerState[] states)
	{
		DateTimeOffset started = _time.GetUtcNow();
		_time.Advance(TimeSpan.FromMilliseconds(200));
		return _loops.Observe(states, started, _time.GetUtcNow(), _changes);
	}
}
