using Microsoft.Extensions.Time.Testing;
using Mokaterm.Abstractions.Containers;
using Mokaterm.DevHost.Demo.Docker;
using Mokaterm.DevHost.Demo.FileSystem;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.Modules.Ssh.Tools;

namespace Mokaterm.Modules.Ssh.Tests.Docker;

// The DevHost's demo engine played through the module's own client, watch and readers: its two crash loops are found,
// their logs end in the crash, and Stop restarting leaves the container down after its next exit.
public sealed class DockerDemoRestartLoopTests
{
	private const string Prometheus = "monitoring-prometheus-1";
	private const string Relay = "mail-relay";

	private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(250);

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task TheWatch_FindsBothLoops_AndKnowsHowTheyEnded()
	{
		(FakeTimeProvider time, DockerSessionFeature feature) = Demo();
		await using (feature)
		{
			DockerHealthWatch watch = new(feature.RestartLoops);
			List<DockerHealthAlert> alerts = [];
			List<DockerContainerState> prometheus = [];
			for (int reads = 0; reads < 9; reads++)
			{
				DateTimeOffset started = time.GetUtcNow();
				IReadOnlyList<DockerContainerState> states = await DriveAsync(time, feature.Client.ContainerStatesAsync(Ct));
				alerts.AddRange(watch.Observe(states, started, time.GetUtcNow(), feature.Changes));
				prometheus.Add(states.Single(state => state.Name == Prometheus));
				time.Advance(TimeSpan.FromSeconds(10));
			}

			Assert.Equal([Relay, Prometheus], feature.RestartLoops.Looping.Select(loop => loop.Name));
			Assert.Contains(prometheus, state => state.State == "running" && state.ExitCode == 0 && !state.OomKilled);
			Assert.Contains(prometheus, state => state.State == "restarting" && state.ExitCode == 137 && state.OomKilled);
			Assert.True(prometheus[^1].RestartCount >= prometheus[0].RestartCount + 6, "the count climbs with every few reads");
			Assert.All(prometheus, state => Assert.Equal("always", state.RestartPolicy));

			Assert.StartsWith($"{Prometheus} is in a restart loop", Assert.Single(alerts, alert => alert.Event == DockerHealthEvent.RestartLoop && alert.Container == Prometheus).Message, StringComparison.Ordinal);
			Assert.Single(alerts, alert => alert.Event == DockerHealthEvent.RestartLoop && alert.Container == Relay);
			Assert.True(feature.RestartLoops.FindLooping(Id(feature, Prometheus))!.LastExit is { ExitCode: 137, OomKilled: true });
			Assert.True(feature.RestartLoops.FindLooping(Id(feature, Relay))!.LastExit is { ExitCode: 1, OomKilled: false });
		}
	}

	[Fact]
	public async Task TheLogs_EndInTheCrash_BeforeTheRunGoingNow()
	{
		(FakeTimeProvider time, DockerSessionFeature feature) = Demo();
		await using (feature)
		{
			IReadOnlyList<LogLine> relay = await DriveAsync(time, feature.Client.LogsAsync(Relay, DockerCommands.RestartLogLines, since: null, Ct));
			DockerContainerDetails relayDetails = await DriveAsync(time, feature.Client.InspectAsync(Relay, Ct));
			IReadOnlyList<LogLine> prometheus = await DriveAsync(time, feature.Client.LogsAsync(Prometheus, DockerCommands.RestartLogLines, since: null, Ct));
			DockerContainerDetails prometheusDetails = await DriveAsync(time, feature.Client.InspectAsync(Prometheus, Ct));

			Assert.InRange(relay.Count, 50, DockerCommands.RestartLogLines);
			Assert.Equal(relay.OrderBy(line => line.Timestamp, StringComparer.Ordinal).Select(line => line.Timestamp), relay.Select(line => line.Timestamp));
			Assert.Contains(relay, line => line.IsError && line.Text == "relay: error: authentication failed: RELAY_PASSWORD is not set");
			IReadOnlyList<LogLine> relayBefore = DockerRestartLoop.BeforeRun(relay, DockerRestartLoop.RunningAgainSince(relayDetails)!.Value)!;
			Assert.Equal("relay: exiting with status 1", relayBefore[^1].Text);
			Assert.True(relayBefore.Count < relay.Count);

			IReadOnlyList<LogLine> prometheusBefore = DockerRestartLoop.BeforeRun(prometheus, DockerRestartLoop.RunningAgainSince(prometheusDetails)!.Value)!;
			Assert.EndsWith("segment=903 maxSegment=1288", prometheusBefore[^1].Text, StringComparison.Ordinal);
		}
	}

	[Fact]
	public async Task Inspect_WhileItWaitsToRestart_SaysItRanOutOfMemory()
	{
		(FakeTimeProvider time, DockerSessionFeature feature) = Demo();
		await using (feature)
		{
			time.Advance(TimeSpan.FromSeconds(5));

			DockerContainerDetails details = await DriveAsync(time, feature.Client.InspectAsync(Prometheus, Ct));

			Assert.Equal("restarting", details.Status);
			Assert.Equal(137, details.ExitCode);
			Assert.True(details.OomKilled);
			Assert.Equal("always", details.RestartPolicy);
			Assert.Equal(TimeSpan.FromSeconds(6), details.FinishedAt - details.StartedAt);
		}
	}

	[Fact]
	public async Task StopRestarting_LeavesItDownAfterItsNextExit()
	{
		(FakeTimeProvider time, DockerSessionFeature feature) = Demo();
		await using (feature)
		{
			DateTimeOffset first = time.GetUtcNow();
			IReadOnlyList<DockerContainerState> before = await DriveAsync(time, feature.Client.ContainerStatesAsync(Ct));
			feature.RestartLoops.Observe(before, first, time.GetUtcNow(), feature.Changes);

			DockerOutput output = await DriveAsync(time, feature.Client.RunAsync(DockerCommands.StopRestarting(Relay), Ct));
			Assert.True(output.Succeeded, output.Error);

			time.Advance(TimeSpan.FromSeconds(30));
			DateTimeOffset second = time.GetUtcNow();
			IReadOnlyList<DockerContainerState> after = await DriveAsync(time, feature.Client.ContainerStatesAsync(Ct));
			feature.RestartLoops.Observe(after, second, time.GetUtcNow(), feature.Changes);
			DockerContainerState relay = after.Single(state => state.Name == Relay);

			Assert.Equal("exited", relay.State);
			Assert.Equal(1, relay.ExitCode);
			Assert.Equal("no", relay.RestartPolicy);
			Assert.False(feature.RestartLoops.IsLooping(relay.Id));

			time.Advance(TimeSpan.FromMinutes(1));
			IReadOnlyList<DockerContainerState> later = await DriveAsync(time, feature.Client.ContainerStatesAsync(Ct));
			Assert.Equal(relay.RestartCount, later.Single(state => state.Name == Relay).RestartCount);
			Assert.Equal("exited", later.Single(state => state.Name == Relay).State);
		}
	}

	[Fact]
	public async Task StartingItAgain_SetsTheCountBackToZero()
	{
		(FakeTimeProvider time, DockerSessionFeature feature) = Demo();
		await using (feature)
		{
			await DriveAsync(time, feature.Client.RunAsync(DockerCommands.Stop(Prometheus), Ct));
			await DriveAsync(time, feature.Client.RunAsync(DockerCommands.Start(Prometheus), Ct));

			IReadOnlyList<DockerContainerState> states = await DriveAsync(time, feature.Client.ContainerStatesAsync(Ct));
			DockerContainerState prometheus = states.Single(state => state.Name == Prometheus);

			Assert.Equal("running", prometheus.State);
			Assert.Equal(0, prometheus.RestartCount);
		}
	}

	private static string Id(DockerSessionFeature feature, string name) =>
		feature.RestartLoops.Looping.Single(loop => loop.Name == name).Id;

	private static (FakeTimeProvider Time, DockerSessionFeature Feature) Demo()
	{
		FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 18, 9, 0, 0, TimeSpan.Zero));
		DemoNode root = DemoNode.NewDirectory("", "root", "root", DemoModes.Octal("755"), time.GetUtcNow().AddDays(-100));
		DemoDockerFiles.Add(root, time.GetUtcNow());
		DemoFileTree tree = new(root, ["root", "demo"], ["root", "docker"], time);
		DemoDockerEngine engine = new(time, DemoDockerFiles.Reader(tree), DemoDockerFiles.ByteReader(tree));
		return (time, DockerSessionFeature.ForCommands(new DemoDockerCommands(engine, time), time, "demo-docker", "demo"));
	}

	private static async Task<T> DriveAsync<T>(FakeTimeProvider time, Task<T> task)
	{
		await Eventually.AdvanceUntilAsync(time, Step, () => task.IsCompleted, "the demo answers", maxSteps: 100);
		return await task;
	}
}
