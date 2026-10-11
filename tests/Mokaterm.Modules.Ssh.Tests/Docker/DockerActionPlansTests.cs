using Mokaterm.Modules.Ssh.Components.Docker;
using Mokaterm.Modules.Ssh.Docker;

namespace Mokaterm.Modules.Ssh.Tests.Docker;

/// <summary>What every button runs, what it is called while it runs, and which ones ask first.</summary>
public sealed class DockerActionPlansTests
{
	private static readonly ComposeTarget Shop = new(new ComposeTool(DockerProgram.ComposePlugin, "2.29.7"), "shop", "/srv/shop", ["/srv/shop/compose.yaml"], []);

	private static readonly string[] Services = ["web", "api", "db"];

	private const string Prefix = "cd -- /srv/shop && docker compose -p shop -f /srv/shop/compose.yaml";

	[Theory]
	[InlineData("restart", null, "Restart shop (web, api, db)", "restart", false)]
	[InlineData("start", null, "Start shop (web, api, db)", "start", false)]
	[InlineData("stop", null, "Stop shop (web, api, db)", "stop", true)]
	[InlineData("pull", null, "Pull images for shop (web, api, db)", "pull", false)]
	[InlineData("recreate", null, "Recreate shop (web, api, db)", "up --detach --force-recreate", true)]
	[InlineData("down", null, "Take down shop", "down", true)]
	[InlineData("down-volumes", null, "Take down shop and its volumes", "down --volumes", true)]
	[InlineData("restart", "api", "Restart api", "restart api", false)]
	[InlineData("stop", "api", "Stop api", "stop api", true)]
	[InlineData("start", "api", "Start api", "start api", false)]
	[InlineData("pull", "api", "Pull images for api", "pull api", false)]
	[InlineData("recreate", "api", "Recreate api", "up --detach --force-recreate --no-deps api", true)]
	public void Compose_EveryAction(string action, string? service, string title, string words, bool asks)
	{
		ComposeActionPlan plan = ComposeActions.Plan(action, Shop, Services, service)!;

		Assert.Equal(title, plan.Title);
		Assert.Equal($"{Prefix} {words}", Assert.Single(plan.Calls).Display);
		Assert.Equal(asks, plan.Confirm is not null);
	}

	[Fact]
	public void Compose_Up_BuildsAndPullsFirstWhenAsked()
	{
		ComposeActionPlan plain = ComposeActions.Plan("up", Shop, Services)!;
		ComposeActionPlan both = ComposeActions.Plan("up", Shop, Services, build: true, pullFirst: true)!;

		Assert.Equal("Start shop (web, api, db)", plain.Title);
		Assert.Equal([$"{Prefix} up --detach"], plain.Calls.Select(call => call.Display));
		Assert.Null(plain.Confirm);
		Assert.Equal("Build and start shop (web, api, db)", both.Title);
		Assert.Equal([$"{Prefix} pull", $"{Prefix} up --detach --build"], both.Calls.Select(call => call.Display));
	}

	[Fact]
	public void Compose_Down_SaysWhetherTheVolumesGo()
	{
		Assert.Contains("Its volumes are kept.", ComposeActions.Plan("down", Shop, Services)!.Confirm, StringComparison.Ordinal);
		Assert.Contains("Whatever they store is deleted.", ComposeActions.Plan("down-volumes", Shop, Services)!.Confirm, StringComparison.Ordinal);
	}

	[Fact]
	public void Compose_AnActionThatRunsNothing_HasNoPlan() => Assert.Null(ComposeActions.Plan("logs", Shop, Services));

	[Theory]
	[InlineData("start", "docker start web", false)]
	[InlineData("restart", "docker restart web", false)]
	[InlineData("pause", "docker pause web", false)]
	[InlineData("unpause", "docker unpause web", false)]
	[InlineData("stop", "docker stop web", true)]
	[InlineData("kill", "docker kill web", true)]
	[InlineData("remove", "docker rm web", true)]
	[InlineData("remove-volumes", "docker rm --volumes web", true)]
	public void Container_EveryAction(string action, string command, bool asks)
	{
		DockerChangePlan plan = DockerContainerActions.Plan(action, "web", alive: false)!;

		Assert.Equal(command, plan.Call.Display);
		Assert.Equal(asks, plan.Confirm is not null);
	}

	[Fact]
	public void Container_RemovingOneThatRuns_KillsItFirst_AndSaysSo()
	{
		DockerChangePlan plan = DockerContainerActions.Plan("remove", "web", alive: true)!;

		Assert.Equal("docker rm --force web", plan.Call.Display);
		Assert.Contains("killed first", plan.Confirm, StringComparison.Ordinal);
		Assert.Contains("Named volumes are kept", plan.Confirm, StringComparison.Ordinal);
	}
}
