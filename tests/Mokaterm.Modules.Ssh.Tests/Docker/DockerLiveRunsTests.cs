using System.Text;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Containers;
using Mokaterm.Modules.Ssh.Docker;

namespace Mokaterm.Modules.Ssh.Tests.Docker;

/// <summary>
/// The session's builds and pushes: one at a time, run in the background with their output as lines, stopped when the
/// user asks or the session goes, worded for how they ended, and noted for the health watch while they run.
/// </summary>
public sealed class DockerLiveRunsTests
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task ARun_ThatSucceeds_KeepsItsLines_AndDropsTheUpdateChecksOfWhatItMade()
	{
		ScriptedShell shell = new() { Lines = ["#1 [internal] load build definition", "#2 DONE 0.1s"] };
		await using DockerSessionFeature feature = Feature(shell);
		DockerLivePlan plan = Plan() with { Refreshes = ["app:1.4"] };
		int forgotten = 0;
		feature.ImageUpdates.Changed += () => Interlocked.Increment(ref forgotten);

		DockerLiveRun run = feature.StartLive(plan, feature.Client);
		await run.Completion.WaitAsync(TimeSpan.FromSeconds(15), Ct);

		Assert.Equal(DockerLiveState.Succeeded, run.State);
		Assert.Equal("exit 0", run.StateText);
		Assert.Null(run.Problem);
		Assert.Equal(["#1 [internal] load build definition", "#2 DONE 0.1s"], run.Lines.Snapshot().Select(line => line.Text));
		Assert.Equal(["docker buildx build --tag app:1.4 /srv/app"], run.Shown);
		Assert.Same(run, feature.LiveRuns.Current);
		await Eventually.TrueAsync(() => Volatile.Read(ref forgotten) == 1, "the update check of app:1.4 is dropped");
	}

	[Fact]
	public async Task OnlyOneRunsAtATime_AndAnEndedOneCanBeDismissed()
	{
		ScriptedShell shell = new() { Hold = true };
		await using DockerSessionFeature feature = Feature(shell);

		DockerLiveRun first = feature.StartLive(Plan(), feature.Client);
		await Eventually.TrueAsync(() => shell.Started, "the first run started");

		Assert.Throws<InvalidOperationException>(() => feature.StartLive(Plan(), feature.Client));
		feature.LiveRuns.Dismiss(first);
		Assert.Same(first, feature.LiveRuns.Current);

		first.Cancel();
		await first.Completion.WaitAsync(TimeSpan.FromSeconds(15), Ct);
		feature.LiveRuns.Dismiss(first);
		Assert.Null(feature.LiveRuns.Current);
	}

	[Fact]
	public async Task Stop_ClosesTheCall_AndSaysWhatThatDoes()
	{
		ScriptedShell shell = new() { Lines = ["#8 [4/5] RUN npm ci"], Hold = true };
		await using DockerSessionFeature feature = Feature(shell);

		DockerLiveRun run = feature.StartLive(Plan(), feature.Client);
		await Eventually.TrueAsync(() => run.Lines.Snapshot().Count == 1, "the first line arrived");
		run.Cancel();
		await run.Completion.WaitAsync(TimeSpan.FromSeconds(15), Ct);

		Assert.Equal(DockerLiveState.Stopped, run.State);
		Assert.True(shell.SawCancel);
		Assert.StartsWith("Stopped.", run.Problem, StringComparison.Ordinal);
		Assert.Equal("stopped", run.StateText);
	}

	[Fact]
	public async Task AFailedBuild_SaysItsLastErrorLine()
	{
		ScriptedShell shell = new()
		{
			ErrorLines = ["#8 [4/5] RUN npm ci", "#8 ERROR: process \"/bin/sh -c npm ci\" did not complete successfully: exit code: 1", "ERROR: failed to solve: process did not complete successfully"],
			Exit = 1,
		};
		await using DockerSessionFeature feature = Feature(shell);
		int forgotten = 0;
		feature.ImageUpdates.Changed += () => Interlocked.Increment(ref forgotten);

		DockerLiveRun run = feature.StartLive(Plan() with { Refreshes = ["app:1.4"] }, feature.Client);
		await run.Completion.WaitAsync(TimeSpan.FromSeconds(15), Ct);

		Assert.Equal(DockerLiveState.Failed, run.State);
		Assert.Equal("ERROR: failed to solve: process did not complete successfully", run.Problem);
		Assert.Equal(1, run.ExitCode);
		await Eventually.StaysTrueAsync(() => Volatile.Read(ref forgotten) == 0, "nothing is dropped after a failure");
	}

	[Fact]
	public async Task ABuildThatRanOutOfTimeOnTheServer_SaysSo()
	{
		ScriptedShell shell = new() { Exit = 124 };
		await using DockerSessionFeature feature = Feature(shell);
		DockerLivePlan plan = Plan() with { Calls = [new DockerBuildSpec { Context = "/srv/app" }.ToCall(DockerBuilder.BuildKit)] };

		DockerLiveRun run = feature.StartLive(plan, feature.Client);
		await run.Completion.WaitAsync(TimeSpan.FromSeconds(15), Ct);

		Assert.Equal(DockerLiveState.Failed, run.State);
		Assert.Equal("It ran past the 2 h the server gives it, so the server stopped it.", run.Problem);
	}

	[Fact]
	public async Task ARun_IsNotedForTheHealthWatch_WhileItRuns()
	{
		ScriptedShell shell = new() { Hold = true };
		await using DockerSessionFeature feature = Feature(shell);
		DockerContainerState web = new() { Id = new string('a', 64), Name = "web", RestartCount = 1, State = "running" };
		DockerLivePlan plan = Plan() with { Calls = [DockerCommands.Restart("web")] };
		DateTimeOffset before = TimeProvider.System.GetUtcNow().AddSeconds(-1);

		DockerLiveRun run = feature.StartLive(plan, feature.Client);
		await Eventually.TrueAsync(() => shell.Started, "the run started");

		Assert.True(feature.Changes.Touched(web, before, TimeProvider.System.GetUtcNow()));
		run.Cancel();
		await run.Completion.WaitAsync(TimeSpan.FromSeconds(15), Ct);
	}

	[Fact]
	public async Task TheSessionGoing_StopsARunStillGoing()
	{
		ScriptedShell shell = new() { Hold = true };
		DockerSessionFeature feature = Feature(shell);

		DockerLiveRun run = feature.StartLive(Plan(), feature.Client);
		await Eventually.TrueAsync(() => shell.Started, "the run started");
		await feature.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15), Ct);

		Assert.True(run.Completion.IsCompleted);
		Assert.Equal(DockerLiveState.Lost, run.State);
		Assert.Throws<ObjectDisposedException>(() => feature.StartLive(Plan(), feature.Client));
	}

	private static DockerSessionFeature Feature(ScriptedShell shell) =>
		new(shell, authenticate: null, TimeProvider.System, "build-server", "alice", files: null);

	private static DockerLivePlan Plan() => new(
		[new DockerCall { Program = DockerProgram.Docker, Arguments = ["buildx", "build", "--tag", "app:1.4", "/srv/app"] }],
		"Build app:1.4",
		"Builds it.",
		"Build",
		DockerLivePlan.ImagesOwner,
		ErrorIsProgress: true);

	/// <summary>
	/// A docker that prints its lines through the call's listener, then either waits for the call to be cancelled
	/// (<see cref="Hold"/>) or exits with <see cref="Exit"/>.
	/// </summary>
	private sealed class ScriptedShell : IDockerShell
	{
		private volatile bool _started;
		private volatile bool _sawCancel;

		public IReadOnlyList<string> Lines { get; init; } = [];

		public IReadOnlyList<string> ErrorLines { get; init; } = [];

		public bool Hold { get; init; }

		public int Exit { get; init; }

		public bool IsElevated => false;

		public bool Started => _started;

		public bool SawCancel => _sawCancel;

		public async Task<DockerOutput> RunAsync(DockerCall call, CancellationToken cancellationToken)
		{
			_started = true;
			foreach (string line in Lines)
			{
				call.OnOutput?.Invoke(CommandStream.Output, Encoding.UTF8.GetBytes(line + "\n"));
			}

			foreach (string line in ErrorLines)
			{
				call.OnOutput?.Invoke(CommandStream.Error, Encoding.UTF8.GetBytes(line + "\n"));
			}

			if (Hold)
			{
				try
				{
					await Task.Delay(Timeout.Infinite, cancellationToken);
				}
				catch (OperationCanceledException)
				{
					_sawCancel = true;
					throw;
				}
			}

			return new DockerOutput { Command = DockerCommandLine.Build(call), ExitCode = Exit, Error = string.Join('\n', ErrorLines) };
		}
	}
}
