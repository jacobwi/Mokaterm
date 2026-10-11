using System.Text.Json;
using Mokaterm.Modules.Ssh.Docker;

namespace Mokaterm.Modules.Ssh.Tests.Docker;

/// <summary>Every listing the manager reads, over what docker prints.</summary>
public sealed class DockerParsersTests
{
	[Fact]
	public void Containers_ReadEveryObjectLine_AndSkipTheWarning()
	{
		IReadOnlyList<DockerContainer> containers = DockerContainer.Parse(DockerFixtures.Containers);

		Assert.Equal(["shop-web-1", "shop-db-1", "a.b_c-d", "monitoring-grafana-1", "shop-web-run-1a2b"], containers.Select(container => container.Name));
	}

	[Fact]
	public void Containers_ReadStateHealthAndPorts()
	{
		IReadOnlyList<DockerContainer> containers = DockerContainer.Parse(DockerFixtures.Containers);
		DockerContainer web = containers[0];
		DockerContainer db = containers[1];
		DockerContainer redis = containers[2];
		DockerContainer grafana = containers[3];

		Assert.Equal("0a1b2c3d4e5f", web.ShortId);
		Assert.True(web.IsRunning);
		Assert.Equal(DockerHealth.Healthy, web.Health);
		Assert.Equal(["8080→80/tcp"], web.Ports.Select(port => port.Display));
		Assert.Equal(DockerHealth.Unhealthy, db.Health);
		Assert.Equal(["127.0.0.1:5432→5432/tcp"], db.Ports.Select(port => port.Display));
		Assert.Equal(["shop_default", "monitoring"], db.NetworkNames);
		Assert.Equal(["shop_pgdata"], db.MountNames);
		Assert.Equal("exited", redis.State);
		Assert.False(redis.IsAlive);
		Assert.Equal(["6379/tcp"], redis.Ports.Select(port => port.Display));
		Assert.False(redis.Ports[0].IsPublished);

		// No State field (an older docker): the status line says it.
		Assert.Equal("paused", grafana.State);
		Assert.True(grafana.IsPaused);
		Assert.True(grafana.IsAlive);
	}

	[Fact]
	public void Containers_ReadComposeLabels_WithCommasInsideAValue()
	{
		IReadOnlyList<DockerContainer> containers = DockerContainer.Parse(DockerFixtures.Containers);
		DockerContainer web = containers[0];

		Assert.Equal("shop", web.ComposeProject);
		Assert.Equal("web", web.ComposeService);
		Assert.Equal("/srv/shop", web.ComposeWorkingDirectory);
		Assert.Equal(["/srv/shop/compose.yaml", "/srv/shop/compose.prod.yaml"], web.ComposeConfigFiles);
		Assert.False(web.IsOneOff);
		Assert.True(containers[4].IsOneOff);
		Assert.Equal(["/opt/monitoring/.env.prod"], containers[3].ComposeEnvironmentFiles);
		Assert.Null(containers[2].ComposeProject);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("\n")]
	[InlineData("[]")]
	[InlineData("not json at all")]
	public void Listings_ThatHoldNothing_AreEmpty(string? output)
	{
		Assert.Empty(DockerContainer.Parse(output));
		Assert.Empty(DockerImage.Parse(output));
		Assert.Empty(DockerVolume.Parse(output, null));
		Assert.Empty(DockerNetwork.Parse(output, []));
		Assert.Empty(DockerContainerStats.Parse(output));
		Assert.Empty(DockerDiskUsage.Parse(output));
		Assert.Empty(ComposeContainer.Parse(output));
		Assert.Empty(ComposeProject.Parse(output, []));
	}

	[Fact]
	public void Inspect_ReadsTheSummary()
	{
		DockerContainerDetails details = DockerInspectParser.Parse(DockerFixtures.Inspect);

		Assert.Equal("shop-db-1", details.Name);
		Assert.Equal("postgres:16", details.Image);
		Assert.Equal("running", details.Status);
		Assert.Equal(4242, details.Pid);
		Assert.Equal("on-failure:3", details.RestartPolicy);
		Assert.Equal(2, details.RestartCount);
		Assert.Equal("postgres", details.User);
		Assert.Equal(["docker-entrypoint.sh"], details.Entrypoint);
		Assert.Equal(["postgres"], details.Command);
		Assert.Equal(DockerHealth.Unhealthy, details.Health!.Status);
		Assert.Equal(3, details.Health.FailingStreak);
		Assert.Equal("pg_isready: no response", Assert.Single(details.Health.Probes).Output);
	}

	[Fact]
	public void Inspect_ReadsPortsMountsAndNetworks()
	{
		DockerContainerDetails details = DockerInspectParser.Parse(DockerFixtures.Inspect);

		Assert.Equal(["127.0.0.1:5432→5432/tcp", "8008/tcp"], details.Ports.Select(port => port.Display));
		Assert.Equal(
			[
				new DockerMount("volume", "shop_pgdata", "/var/lib/docker/volumes/shop_pgdata/_data", "/var/lib/postgresql/data", "z", true),
				new DockerMount("bind", "", "/srv/shop/initdb", "/docker-entrypoint-initdb.d", "ro", false),
			],
			details.Mounts);
		DockerNetworkAttachment network = Assert.Single(details.Networks);
		Assert.Equal("shop_default", network.Network);
		Assert.Equal("172.18.0.3", network.IpAddress);
		Assert.Equal(["shop-db-1", "db"], network.Aliases);
	}

	[Fact]
	public void Inspect_KeepsTheEnvironment_ButNeverPrintsAValue()
	{
		DockerContainerDetails details = DockerInspectParser.Parse(DockerFixtures.Inspect);
		DockerVariable password = details.Environment[0];

		Assert.Equal("POSTGRES_PASSWORD", password.Name);
		Assert.Equal("s3cr3t-demo", password.Value);
		Assert.Equal("", details.Environment[2].Value);
		Assert.DoesNotContain("s3cr3t", password.ToString(), StringComparison.Ordinal);
	}

	[Fact]
	public void InspectRawJson_MasksEveryEnvironmentValue_UnlessRevealed()
	{
		DockerContainerDetails details = DockerInspectParser.Parse(DockerFixtures.Inspect);

		string masked = DockerInspectParser.RawJson(details.Raw, revealEnvironment: false);
		string revealed = DockerInspectParser.RawJson(details.Raw, revealEnvironment: true);

		Assert.DoesNotContain("s3cr3t-demo", masked, StringComparison.Ordinal);
		Assert.Contains($"\"POSTGRES_PASSWORD={DockerInspectParser.Mask}\"", masked, StringComparison.Ordinal);
		Assert.Contains("\"EMPTY=\"", masked, StringComparison.Ordinal);

		// Only Config.Env is masked: a label or a mount that happens to look like a variable stays as it is.
		Assert.Contains("\"/var/lib/docker/volumes/shop_pgdata/_data\"", masked, StringComparison.Ordinal);
		Assert.Contains("POSTGRES_PASSWORD=s3cr3t-demo", revealed, StringComparison.Ordinal);
		Assert.DoesNotContain("\r", masked, StringComparison.Ordinal);
		using JsonDocument reparsed = JsonDocument.Parse(masked);
		Assert.Equal("f27c9f265ce1aa", reparsed.RootElement.GetProperty("Id").GetString());
	}

	[Fact]
	public void Inspect_SeveralContainers_AndNone()
	{
		string two = "[" + DockerFixtures.Inspect.Trim().TrimStart('[').TrimEnd(']') + "," + DockerFixtures.Inspect.Trim().TrimStart('[').TrimEnd(']').Replace("shop-db-1", "shop-db-2", StringComparison.Ordinal) + "]";

		Assert.Equal(["shop-db-1", "shop-db-2"], DockerInspectParser.ParseAll(two).Select(details => details.Name));
		Assert.Throws<DockerCommandException>(() => DockerInspectParser.Parse("[]"));
	}

	[Fact]
	public void Images_ReadTagsDigestsAndDanglingOnes()
	{
		IReadOnlyList<DockerImage> images = DockerImage.Parse(DockerFixtures.Images);

		Assert.Equal("nginx:1.27-alpine", images[0].Reference);
		Assert.Equal("aaaaaaaaaaaa", images[0].ShortId);
		Assert.Equal(48_300_000, images[0].SizeBytes);
		Assert.False(images[0].IsDangling);
		Assert.Equal("nginx:1.27-alpine", images[0].RemoveTarget);

		Assert.True(images[1].IsDangling);
		Assert.Equal("4ca2d821beb2", images[1].Reference);
		Assert.Equal(images[1].Id, images[1].RemoveTarget);

		Assert.Equal("registry.local:5000/team/app@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", images[2].Reference);
		Assert.Equal(1_200_000_000, images[2].SizeBytes);
	}

	[Fact]
	public void Volumes_AreUnused_WhenTheDanglingListNamesThem()
	{
		IReadOnlyList<DockerVolume> volumes = DockerVolume.Parse(DockerFixtures.Volumes, "old-cache\n2e96105c29d2\n");

		Assert.True(volumes.Single(volume => volume.Name == "shop_pgdata").InUse);
		Assert.False(volumes.Single(volume => volume.Name == "old-cache").InUse);
		Assert.Equal("/var/lib/docker/volumes/shop_pgdata/_data", volumes[0].Mountpoint);
	}

	[Fact]
	public void Networks_KnowWhoUsesThem_AndWhichAreBuiltIn()
	{
		IReadOnlyList<DockerNetwork> networks = DockerNetwork.Parse(DockerFixtures.Networks, DockerContainer.Parse(DockerFixtures.Containers));

		DockerNetwork shop = networks.Single(network => network.Name == "shop_default");
		Assert.Equal(["shop-web-1", "shop-db-1", "shop-web-run-1a2b"], shop.Containers);
		Assert.False(shop.IsRemovable);
		Assert.Equal("4aecec001122", shop.ShortId);
		Assert.True(networks.Single(network => network.Name == "host").IsBuiltIn);
		Assert.False(networks.Single(network => network.Name == "bridge").IsRemovable);
		DockerNetwork legacy = networks.Single(network => network.Name == "legacy_net");
		Assert.True(legacy.Internal);
		Assert.True(legacy.IsRemovable);
	}

	[Fact]
	public void Stats_ReadOneSample_AndAStoppedContainersDashes()
	{
		IReadOnlyList<DockerContainerStats> stats = DockerContainerStats.Parse(DockerFixtures.Stats);

		Assert.Equal(0.52, stats[0].CpuValue);
		Assert.Equal(1.23, stats[0].MemoryPercentValue);
		Assert.Equal("10.2MiB / 7.6GiB", stats[0].Memory);
		Assert.Equal("9f8e7d6c5b4a", stats[1].Id);
		Assert.Null(stats[1].CpuValue);
	}

	[Fact]
	public void DiskUsage_ReadsWhatPruningCouldFree()
	{
		IReadOnlyList<DockerDiskUsage> usage = DockerDiskUsage.Parse(DockerFixtures.DiskUsage);

		Assert.Equal(["Images", "Containers", "Local Volumes", "Build Cache"], usage.Select(row => row.Type));
		Assert.Equal(282_300_000, usage[0].ReclaimableBytes);
		Assert.Equal(18_000, usage[1].ReclaimableBytes);
		Assert.Equal(642_000_000, usage[3].ReclaimableBytes);
	}

	[Theory]
	[InlineData("1.5GiB", 1_610_612_736L)]
	[InlineData("10.2MiB", 10_695_475L)]
	[InlineData("0B", 0L)]
	[InlineData("1.2kB", 1_200L)]
	[InlineData("32.5MB (virtual 187MB)", 32_500_000L)]
	[InlineData("N/A", null)]
	[InlineData("", null)]
	[InlineData("-1MB", null)]
	public void Sizes_DockerWritesThemInBothBases(string text, long? expected) => Assert.Equal(expected, DockerSizes.Parse(text));

	[Theory]
	[InlineData("0.0.0.0:8080->80/tcp, :::8080->80/tcp", new[] { "8080→80/tcp" })]
	[InlineData("[::]:8443->443/tcp", new[] { "8443→443/tcp" })]
	[InlineData("127.0.0.1:53->53/udp, 0.0.0.0:53->53/tcp", new[] { "127.0.0.1:53→53/udp", "53→53/tcp" })]
	[InlineData("80/tcp, 443", new[] { "80/tcp", "443/tcp" })]
	[InlineData("garbage->", new string[0])]
	public void Ports_FoldTheSameMappingOnBothAddressFamilies(string text, string[] expected) =>
		Assert.Equal(expected, DockerPorts.Parse(text).Select(port => port.Display));

	[Fact]
	public void ComposePs_OneObjectPerLine_WithPublishers()
	{
		IReadOnlyList<ComposeContainer> containers = ComposeContainer.Parse(DockerFixtures.ComposePsLines);

		Assert.Equal(["shop-web-1", "shop-web-2", "shop-worker.v2-1"], containers.Select(container => container.Name));
		Assert.Equal(["8080→80/tcp", "443/tcp"], containers[0].Ports.Select(port => port.Display));
		Assert.Equal(DockerHealth.Healthy, containers[0].Health);
		Assert.Empty(containers[1].Ports);
		Assert.Equal("worker.v2", containers[2].Service);
		Assert.Equal(1, containers[2].ExitCode);
		Assert.False(containers[2].IsRunning);
	}

	[Fact]
	public void ComposePs_OneArray_AsOlderReleasesPrintIt()
	{
		IReadOnlyList<ComposeContainer> containers = ComposeContainer.Parse(DockerFixtures.ComposePsArray);

		Assert.Equal(["shop-db-1", "shop-api-1"], containers.Select(container => container.Name));
		Assert.Equal(DockerHealth.Unhealthy, containers[0].Health);
		Assert.Equal(["127.0.0.1:5432→5432/tcp"], containers[0].Ports.Select(port => port.Display));
		Assert.Equal("restarting", containers[1].State);
	}

	[Fact]
	public void VolumeInspect_GivesTheHostPath()
	{
		DockerVolumeDetails details = DockerVolumeDetails.Parse(DockerFixtures.VolumeInspect)!;

		Assert.Equal("shop_pgdata", details.Name);
		Assert.Equal("/var/lib/docker/volumes/shop_pgdata/_data", details.Mountpoint);
		Assert.Equal("local", details.Driver);
		Assert.Equal("pgdata", details.Labels["com.docker.compose.volume"]);
		Assert.Empty(details.Options);
		Assert.Null(DockerVolumeDetails.Parse("[]"));
	}

	[Fact]
	public void VolumeUse_MapsEachMountToItsPathInsideTheContainer()
	{
		IReadOnlyList<DockerContainerDetails> containers = DockerInspectParser.ParseAll(DockerFixtures.Inspect);

		DockerVolumeUse use = Assert.Single(DockerVolumeUse.Find(containers, "shop_pgdata"));

		Assert.Equal(new DockerVolumeUse("shop-db-1", "running", "/var/lib/postgresql/data", true), use);

		// A bind mount whose source happens to sit under a volume's folder is not a use of that volume.
		Assert.Empty(DockerVolumeUse.Find(containers, "initdb"));
	}
}
