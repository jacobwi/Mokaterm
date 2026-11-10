using Microsoft.Extensions.Time.Testing;
using Mokaterm.Abstractions.Containers;
using Mokaterm.Modules.Ssh.Components.Docker;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.Modules.Ssh.Tests.Fakes;

namespace Mokaterm.Modules.Ssh.Tests.Docker;

/// <summary>
/// What the manager's lists show beside docker's own columns, and what their boxes do: one command for every container
/// picked, through the same plans and question a row's own action has, which the health watch reads like any other.
/// </summary>
public sealed class DockerTableRowsTests
{
	[Fact]
	public void SeveralContainers_GoInOneCommand_EachByName()
	{
		Assert.Equal("docker start web db", DockerCommands.Start(["web", "db"]).Display);
		Assert.Equal("docker stop web db", DockerCommands.Stop(["web", "db"]).Display);
		Assert.Equal("docker restart web db", DockerCommands.Restart(["web", "db"]).Display);
		Assert.Equal("docker rm web db", DockerCommands.Remove(["web", "db"], force: false).Display);
		Assert.Equal("docker rm --force web db", DockerCommands.Remove(["web", "db"], force: true).Display);
		Assert.Equal(DockerCall.ChangeTimeout, DockerCommands.Stop(["web", "db"]).Timeout);
		Assert.Throws<ArgumentOutOfRangeException>(() => DockerCommands.Stop([]));
		Assert.Throws<ArgumentException>(() => DockerCommands.Stop(["web", "-it"]));
	}

	[Fact]
	public void TheHealthWatch_SeesEveryContainerOfABulkChange()
	{
		DockerChangeScope scope = DockerChangeScope.Of(DockerCommands.Remove(["web", "db"], force: true));

		Assert.Equal(["db", "web"], scope.Containers.Order(StringComparer.Ordinal));
	}

	[Fact]
	public void BulkStart_StartsOnlyTheStoppedOnes_AndAsksOnceNamingThem()
	{
		DockerChangePlan plan = DockerContainerActions.PlanMany("start", [("web", true), ("db", false), ("cache", false)])!;

		Assert.Equal("docker start db cache", plan.Call.Display);
		Assert.Equal("Start 2 containers", plan.Title);
		Assert.Equal("These 2 containers are started: db, cache.", plan.Confirm);
		Assert.False(plan.Destructive);
	}

	[Fact]
	public void BulkStop_StopsOnlyTheOnesThatRun()
	{
		DockerChangePlan plan = DockerContainerActions.PlanMany("stop", [("web", true), ("db", true), ("cache", false)])!;

		Assert.Equal("docker stop web db", plan.Call.Display);
		Assert.Contains("web, db", plan.Confirm, StringComparison.Ordinal);
		Assert.True(plan.Destructive);
	}

	[Fact]
	public void BulkRemove_KillsTheRunningOnesFirst_AndSaysWhich()
	{
		DockerChangePlan plan = DockerContainerActions.PlanMany("remove", [("web", true), ("db", false), ("cache", false)])!;

		Assert.Equal("docker rm --force web db cache", plan.Call.Display);
		Assert.Equal("Remove 3 containers", plan.Title);
		Assert.Contains("web is running, so it is killed first.", plan.Confirm, StringComparison.Ordinal);

		DockerChangePlan stopped = DockerContainerActions.PlanMany("remove", [("db", false), ("cache", false)])!;
		Assert.Equal("docker rm db cache", stopped.Call.Display);
		Assert.DoesNotContain("killed", stopped.Confirm, StringComparison.Ordinal);
	}

	[Fact]
	public void ABulkActionThatFitsOneContainer_IsThatRowsOwnAction()
	{
		DockerChangePlan plan = DockerContainerActions.PlanMany("restart", [("web", true)])!;

		DockerChangePlan own = DockerContainerActions.Plan("restart", "web", alive: true)!;
		Assert.Equal(own.Call.Display, plan.Call.Display);
		Assert.Equal(own.Title, plan.Title);
		Assert.Null(plan.Confirm);
		Assert.Equal("docker stop web", DockerContainerActions.PlanMany("stop", [("web", true), ("db", false)])!.Call.Display);
	}

	[Fact]
	public void ABulkActionThatFitsNoContainer_IsNothing()
	{
		Assert.Null(DockerContainerActions.PlanMany("start", [("web", true), ("db", true)]));
		Assert.Null(DockerContainerActions.PlanMany("stop", [("web", false)]));
		Assert.Null(DockerContainerActions.PlanMany("pause", [("web", true), ("db", true)]));
	}

	[Theory]
	[InlineData("running", "Up 2 days (healthy)", "Healthy", "Up 2 days")]
	[InlineData("running", "Up 2 days (unhealthy)", "Unhealthy", "Up 2 days")]
	[InlineData("running", "Up 3 seconds (health: starting)", "Starting", "Up 3 seconds")]
	[InlineData("running", "Up Less than a second", "Running", "Up Less than a second")]
	[InlineData("paused", "Up 15 days (Paused)", "Paused", "Up 15 days")]
	[InlineData("exited", "Exited (137) 3 hours ago", "Exited (137)", "3 hours ago")]
	[InlineData("restarting", "Restarting (1) 2 seconds ago", "Restarting", "2 seconds ago")]
	[InlineData("created", "Created", "Created", "")]
	[InlineData("dead", "Dead", "Dead", "")]
	public void TheStateTag_SaysTheStateOnce_AndDockersTimeGoesBesideIt(string state, string status, string tag, string since)
	{
		DockerContainer container = Container("web", state, status);

		Assert.Equal(tag, DockerStateDisplay.TagText(container));
		Assert.Equal(since, DockerStateDisplay.Since(container));
	}

	[Fact]
	public void TheStatusColumn_PutsWhatNeedsALookFirst()
	{
		DockerContainer[] containers =
		[
			Container("exited", "exited", "Exited (0) 1 hour ago"),
			Container("fine", "running", "Up 2 days (healthy)"),
			Container("sick", "running", "Up 2 days (unhealthy)"),
			Container("looping", "restarting", "Restarting (1) 2 seconds ago"),
			Container("paused", "paused", "Up 1 day (Paused)"),
		];

		Assert.Equal(["sick", "looping", "paused", "fine", "exited"], containers.OrderBy(DockerStateDisplay.Rank).Select(container => container.Name));
	}

	[Fact]
	public void AnImage_IsUsedByTheContainersMadeFromItsTag_OrFromItsIdOnceTheTagMovedOn()
	{
		DockerContainer[] containers =
		[
			Container("web", "running", "Up", image: "nginx:1.27-alpine"),
			Container("proxy", "exited", "Exited (0)", image: "docker.io/library/nginx:1.27-alpine"),
			Container("old", "exited", "Exited (0)", image: "4ca2d821beb2"),
			Container("cache", "running", "Up", image: "redis"),
		];

		DockerImage nginx = new() { Id = "sha256:aaaa", Repository = "nginx", Tag = "1.27-alpine" };
		DockerImage dangling = new() { Id = "sha256:4ca2d821beb2aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", Repository = "<none>", Tag = "<none>" };
		DockerImage redis = new() { Id = "sha256:bbbb", Repository = "redis", Tag = "latest" };
		DockerImage unused = new() { Id = "sha256:cccc", Repository = "busybox", Tag = "latest" };

		Assert.Equal(["proxy", "web"], DockerUsers.OfImage(nginx, containers));
		Assert.Equal(["old"], DockerUsers.OfImage(dangling, containers));
		Assert.Equal(["cache"], DockerUsers.OfImage(redis, containers));
		Assert.Empty(DockerUsers.OfImage(unused, containers));
	}

	[Fact]
	public void AVolume_IsUsedByTheContainersThatMountIt()
	{
		IReadOnlyDictionary<string, IReadOnlyList<string>> users = DockerUsers.OfVolumes(
		[
			Container("shop-db-1", "running", "Up", mounts: "shop_pgdata,/srv/shop/init.sql"),
			Container("backup", "exited", "Exited (0)", mounts: "shop_pgdata"),
			Container("web", "running", "Up"),
		]);

		Assert.Equal(["backup", "shop-db-1"], users["shop_pgdata"]);
		Assert.False(users.ContainsKey("web"));
	}

	[Fact]
	public async Task VolumeSizes_AreMeasuredOnlyWhenTheLastReadIsOld()
	{
		FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero));
		FakeDockerShell shell = new FakeDockerShell()
			.Answer(DockerCommandLine.Build(DockerCommands.SystemUsage()), 0, """{"Images":[],"Containers":[],"Volumes":[{"Name":"old-cache","Links":"0","Size":"212MB"}],"BuildCache":[]}""");
		DockerUsageCache cache = new(time);

		Assert.True(cache.IsStale);
		DockerSystemUsage? usage = await cache.MeasureAsync(new DockerClient(shell, "build-server"), TestContext.Current.CancellationToken);

		Assert.Equal(212_000_000, Assert.Single(usage!.Volumes).SizeBytes);
		Assert.Same(usage, cache.Usage);
		Assert.False(cache.IsStale);
		time.Advance(DockerUsageCache.MaxAge);
		Assert.True(cache.IsStale);
	}

	private static DockerContainer Container(string name, string state, string status, string image = "alpine:3.20", string mounts = "") =>
		DockerContainer.FromRow(new DockerContainerRow { Id = name + "-id", Name = name, Image = image, State = state, Status = status, Mounts = mounts });
}
