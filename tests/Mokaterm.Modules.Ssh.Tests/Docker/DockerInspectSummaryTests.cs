using Moka.Red.Core.Enums;
using Mokaterm.Modules.Ssh.Components.Docker;
using Mokaterm.Modules.Ssh.Docker;

namespace Mokaterm.Modules.Ssh.Tests.Docker;

/// <summary>The tiles and fact groups a container's Inspect tab draws before its raw JSON.</summary>
public sealed class DockerInspectSummaryTests
{
	private static readonly DateTimeOffset Now = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

	[Fact]
	public void ARunningContainer_SaysHowLongItIsUp_AndItsHealth()
	{
		DockerContainerDetails details = DockerInspectParser.Parse(DockerFixtures.Inspect);

		IReadOnlyList<InspectTile> tiles = DockerInspectSummary.Tiles(details, Now);

		Assert.Equal(["State", "Up", "Restarts", "Health"], tiles.Select(tile => tile.Label));
		Assert.Equal("Running", tiles[0].Value);
		Assert.Equal("PID 4242", tiles[0].Help);
		Assert.Equal(MokaColor.Warning, tiles[0].Color);
		Assert.Equal("11d 1h", tiles[1].Value);
		Assert.Equal("2", tiles[2].Value);
		Assert.Equal(MokaColor.Warning, tiles[2].Color);
		Assert.Equal(("Unhealthy", "3 failing in a row", MokaColor.Error), (tiles[3].Value, tiles[3].Help, tiles[3].Color));
	}

	[Fact]
	public void AContainerKilledOutOfMemory_IsRed_AndSaysWhenItStopped()
	{
		DockerContainerDetails details = new()
		{
			Id = "abc",
			Name = "worker",
			Status = "exited",
			ExitCode = 137,
			OomKilled = true,
			StartedAt = Now.AddHours(-3),
			FinishedAt = Now.AddMinutes(-20),
		};

		IReadOnlyList<InspectTile> tiles = DockerInspectSummary.Tiles(details, Now);

		Assert.Equal(("Exited", "exit 137, killed out of memory", MokaColor.Error), (tiles[0].Value, tiles[0].Help, tiles[0].Color));
		Assert.Equal(("Stopped", "20 min ago", "after running 2h 40m"), (tiles[1].Label, tiles[1].Value, tiles[1].Help));
		Assert.Equal("policy no", tiles[2].Help);
		Assert.DoesNotContain(tiles, tile => tile.Label == "Health");
	}

	[Fact]
	public void TheFacts_AreGrouped_AndMountsSayWhereTheyComeFrom()
	{
		DockerContainerDetails details = DockerInspectParser.Parse(DockerFixtures.Inspect);

		Assert.Contains(DockerInspectSummary.Runtime(details), fact => fact.Label == "Network mode" && fact.Value == "shop_default");
		Assert.Contains(DockerInspectSummary.Command(details), fact => fact.Label == "Image id" && fact.Value == "4ca2d821beb2");
		Assert.Equal(
			["/var/lib/postgresql/data", "/docker-entrypoint-initdb.d"],
			DockerInspectSummary.Mounts(details).Select(fact => fact.Label));
		Assert.Equal("bind · /srv/shop/initdb · read-only", DockerInspectSummary.Mounts(details)[1].Value);
		Assert.Equal("172.18.0.3 · shop-db-1, db", DockerInspectSummary.Networks(details).Single().Value);
		Assert.Equal("not published", DockerInspectSummary.Ports(details).Single(fact => fact.Label == "8008/tcp").Value);
	}
}
