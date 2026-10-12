using Microsoft.Extensions.Time.Testing;
using Mokaterm.Modules.Ssh.Docker;

namespace Mokaterm.Modules.Ssh.Tests.Docker;

/// <summary>The health watch: what it reads, what counts as news, and that each event is told once and never the user's own.</summary>
public sealed class DockerHealthWatchTests
{
	private const string WebId = "0a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f9";

	private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 11, 7, 0, 0, TimeSpan.Zero));
	private readonly DockerHealthWatch _watch = new();
	private readonly DockerChangeLog _changes;

	public DockerHealthWatchTests() => _changes = new DockerChangeLog(_time);

	[Fact]
	public void Parse_ReadsEachLine_AndSkipsWhatIsNotOne()
	{
		const string Output = $"""
			{WebId} 0 running 0 healthy /shop-web-1 shop
			9f8e7d6c5b4a 3 restarting 137 - /worker
			Error: No such container: 1122334455667788
			aabbccddeeff 0 exited 1 unhealthy /old
			""";

		IReadOnlyList<DockerContainerState> states = DockerContainerState.Parse(Output);

		Assert.Equal(3, states.Count);
		Assert.Equal("shop-web-1", states[0].Name);
		Assert.Equal("shop", states[0].ComposeProject);
		Assert.Equal(DockerHealth.Healthy, states[0].Health);
		Assert.Equal(3, states[1].RestartCount);
		Assert.Equal(DockerHealth.None, states[1].Health);
		Assert.Null(states[1].ComposeProject);
		Assert.True(states[1].IsAlive);
		Assert.False(states[2].IsAlive);
	}

	[Fact]
	public void TheFirstRead_IsOnlyTheBaseline()
	{
		Assert.Empty(Observe(State("web", health: DockerHealth.Unhealthy), State("db", state: "exited", exit: 1)));
	}

	[Fact]
	public void TurningUnhealthy_IsToldOnce()
	{
		Observe(State("web", health: DockerHealth.Healthy));

		DockerHealthAlert alert = Assert.Single(Observe(State("web", health: DockerHealth.Unhealthy)));
		Assert.Equal(DockerHealthEvent.TurnedUnhealthy, alert.Event);
		Assert.Contains("web turned unhealthy", alert.Message, StringComparison.Ordinal);
		Assert.Empty(Observe(State("web", health: DockerHealth.Unhealthy)));
	}

	[Fact]
	public void StoppingOnItsOwn_IsTold_WithItsExitCode()
	{
		Observe(State("web"));

		DockerHealthAlert alert = Assert.Single(Observe(State("web", state: "exited", exit: 137)));
		Assert.Equal(DockerHealthEvent.Exited, alert.Event);
		Assert.True(alert.IsError);
		Assert.Contains("exit code 137", alert.Message, StringComparison.Ordinal);
		Assert.Empty(Observe(State("web", state: "exited", exit: 137)));
	}

	[Fact]
	public void ARestartByTheDaemon_IsTold_ThenQuietForAWhile_ThenCounted()
	{
		Observe(State("worker", restarts: 3));

		DockerHealthAlert first = Assert.Single(Observe(State("worker", restarts: 4)));
		Assert.Equal(DockerHealthEvent.Restarted, first.Event);
		Assert.Contains("once", first.Message, StringComparison.Ordinal);
		Assert.Contains("4 times in all", first.Message, StringComparison.Ordinal);

		_time.Advance(TimeSpan.FromSeconds(10));
		Assert.Empty(Observe(State("worker", restarts: 5)));
		_time.Advance(TimeSpan.FromSeconds(10));
		Assert.Empty(Observe(State("worker", restarts: 6)));

		_time.Advance(DockerHealthWatch.RestartQuietFor);
		DockerHealthAlert later = Assert.Single(Observe(State("worker", restarts: 7)));
		Assert.Contains("3 times", later.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void ANewContainer_AndOneThatWentAway_AreNotNews()
	{
		Observe(State("web"));

		Assert.Empty(Observe(State("web"), State("fresh", state: "exited", exit: 1)));
		Assert.Empty(Observe(State("fresh", state: "exited", exit: 1)));
	}

	[Fact]
	public void TheUsersOwnStop_IsNotNews_ButAnotherContainersCrashIs()
	{
		Observe(State("web"), State("db"));

		using (_changes.Begin([DockerCommands.Stop("web")]))
		{
			IReadOnlyList<DockerHealthAlert> alerts = Observe(State("web", state: "exited"), State("db", state: "exited", exit: 1));
			Assert.Equal("db", Assert.Single(alerts).Container);
		}
	}

	[Fact]
	public void AChangeThatEndedBeforeTheRead_StillCoversWhatItDid()
	{
		Observe(State("web", project: "shop"));
		using (_changes.Begin([DockerCall.Script("true", [])]))
		{
			_time.Advance(TimeSpan.FromSeconds(1));
		}

		_time.Advance(TimeSpan.FromSeconds(5));
		Assert.Empty(Observe(State("web", project: "shop", state: "exited")));

		_time.Advance(TimeSpan.FromSeconds(10));
		Observe(State("web", project: "shop"));
		Assert.Single(Observe(State("web", project: "shop", state: "exited")));
	}

	[Fact]
	public void Scope_ReadsWhatACommandTouches()
	{
		DockerChangeScope remove = DockerChangeScope.Of(DockerCommands.Remove("web", removeVolumes: true, force: true));
		Assert.Equal("web", Assert.Single(remove.Containers));

		ComposeTarget shop = new(new ComposeTool(DockerProgram.ComposePlugin, "2.29.7"), "shop", "/srv/shop", ["/srv/shop/compose.yaml"], []);
		DockerChangeScope compose = DockerChangeScope.Of([ComposeCommands.Pull(shop), ComposeCommands.Up(shop)]);
		Assert.Equal("shop", Assert.Single(compose.Projects));
		Assert.False(compose.All);

		Assert.Same(DockerChangeScope.Nothing, DockerChangeScope.Of(DockerCommands.Pull("nginx")));
		Assert.Same(DockerChangeScope.Nothing, DockerChangeScope.Of(new DockerRunSpec { Image = "nginx" }.ToCall()));
		Assert.True(DockerChangeScope.Of(ComposeCommands.Up(ComposeTarget.ForFile(shop.Tool, "/opt/new/compose.yaml"))).All);
		Assert.True(DockerChangeScope.Of(DockerCall.Script("true", [])).All);
	}

	[Fact]
	public void Scope_CoversByNameIdOrProject()
	{
		DockerContainerState web = State("web", project: "shop") with { Id = WebId };

		Assert.True(DockerChangeScope.Of(DockerCommands.Stop("web")).Covers(web));
		Assert.True(DockerChangeScope.Of(DockerCommands.Stop(WebId[..12])).Covers(web));
		Assert.False(DockerChangeScope.Of(DockerCommands.Stop("db")).Covers(web));
		Assert.True(new DockerChangeScope { Projects = new HashSet<string>(["shop"]) }.Covers(web));
		Assert.True(DockerChangeScope.Everything.Covers(web));
	}

	private IReadOnlyList<DockerHealthAlert> Observe(params DockerContainerState[] states)
	{
		DateTimeOffset started = _time.GetUtcNow();
		_time.Advance(TimeSpan.FromMilliseconds(200));
		return _watch.Observe(states, started, _time.GetUtcNow(), _changes);
	}

	private static DockerContainerState State(string name, string state = "running", int exit = 0, int restarts = 0, DockerHealth health = DockerHealth.None, string? project = null) => new()
	{
		Id = "id-" + name,
		Name = name,
		State = state,
		ExitCode = exit,
		RestartCount = restarts,
		Health = health,
		ComposeProject = project,
	};
}
