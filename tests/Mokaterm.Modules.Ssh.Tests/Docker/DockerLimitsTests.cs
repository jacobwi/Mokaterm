using Mokaterm.Abstractions.Containers;
using Mokaterm.Modules.Ssh.Docker;

namespace Mokaterm.Modules.Ssh.Tests.Docker;

// A container's limits read from docker inspect, and the docker update a change to them becomes: only what changed,
// each flag one word, and refused before docker is asked where docker would refuse.
public sealed class DockerLimitsTests
{
	private const string Inspected = """
		[{
		  "Id": "f27c9f265ce1aa", "Name": "/shop-db-1",
		  "State": { "Status": "running" },
		  "HostConfig": {
		    "NanoCpus": 1500000000, "CpuQuota": 0, "CpuPeriod": 0,
		    "Memory": 536870912, "MemorySwap": 1073741824, "AutoRemove": false,
		    "RestartPolicy": { "Name": "on-failure", "MaximumRetryCount": 3 }
		  },
		  "Config": { "Image": "postgres:16", "Labels": { "com.docker.compose.project": "shop" } }
		}]
		""";

	private static readonly DockerContainerLimits Limited = new()
	{
		NanoCpus = 1_500_000_000,
		MemoryBytes = 512L << 20,
		MemorySwapBytes = 1L << 30,
		RestartPolicy = DockerRestartPolicy.OnFailure,
		RestartRetries = 3,
	};

	[Fact]
	public void Inspect_ReadsTheLimitsAndThePolicy()
	{
		DockerContainerLimits limits = DockerInspectParser.Parse(Inspected).Limits;

		Assert.Equal(1.5m, limits.Cpus);
		Assert.Equal(512L << 20, limits.MemoryBytes);
		Assert.Equal(1L << 30, limits.MemorySwapBytes);
		Assert.Equal(DockerRestartPolicy.OnFailure, limits.RestartPolicy);
		Assert.Equal(3, limits.RestartRetries);
		Assert.False(limits.AutoRemove);
	}

	[Fact]
	public void Inspect_WithoutLimits_ReadsAsNone()
	{
		DockerContainerLimits limits = DockerInspectParser.Parse("""[{"Id":"abc123abc123","Name":"/web","HostConfig":{"RestartPolicy":{"Name":""}}}]""").Limits;

		Assert.Null(limits.Cpus);
		Assert.Equal(0, limits.MemoryBytes);
		Assert.Equal(DockerRestartPolicy.No, limits.RestartPolicy);
	}

	[Fact]
	public void TheForm_StartsAtTheContainersOwnValues_AndChangesNothingUntoldTo()
	{
		DockerUpdateSpec spec = DockerUpdateSpec.From(Limited);

		Assert.Equal("1.5", spec.Cpus);
		Assert.Equal("512m", spec.Memory);
		Assert.Equal("1g", spec.MemorySwap);
		Assert.Equal(DockerRestartPolicy.OnFailure, spec.Restart);
		Assert.Equal(3, spec.MaxRetries);
		Assert.False(spec.HasChanges);
		Assert.Empty(spec.Problems());
		Assert.Throws<ArgumentException>(() => spec.Arguments("shop-db-1"));
	}

	[Fact]
	public void OnlyWhatChanged_GoesOnTheLine_EachFlagAsOneWord()
	{
		DockerUpdateSpec spec = DockerUpdateSpec.From(Limited) with { Memory = "2g", MemorySwap = "-1", Restart = DockerRestartPolicy.UnlessStopped };

		DockerCall call = spec.ToCall("shop-db-1", hostCpus: 4);

		Assert.Equal("docker update --memory=2g --memory-swap=-1 --restart=unless-stopped shop-db-1", call.Display);
		Assert.Equal(DockerCall.ChangeTimeout, call.Timeout);
		Assert.Equal("shop-db-1", Assert.Single(DockerChangeScope.Of(call).Containers));
	}

	[Fact]
	public void ANewLimit_OnAContainerWithNone_IsAdded()
	{
		DockerUpdateSpec spec = DockerUpdateSpec.From(DockerContainerLimits.None) with { Cpus = "0.5", Memory = "256 m", Restart = DockerRestartPolicy.OnFailure, MaxRetries = 5 };

		Assert.Equal("docker update --cpus=0.5 --memory=256m --restart=on-failure:5 web", spec.ToCall("web").Display);
	}

	[Fact]
	public void ALimit_CannotBeTakenAway()
	{
		IReadOnlyDictionary<string, string> problems = (DockerUpdateSpec.From(Limited) with { Cpus = "", Memory = " ", MemorySwap = "" }).Problems();

		Assert.StartsWith("docker update cannot take a CPU limit away", problems[DockerUpdateSpec.CpusField], StringComparison.Ordinal);
		Assert.StartsWith("docker update cannot take a memory limit away", problems[DockerUpdateSpec.MemoryField], StringComparison.Ordinal);
		Assert.StartsWith("docker update cannot take a swap limit away", problems[DockerUpdateSpec.SwapField], StringComparison.Ordinal);
	}

	[Fact]
	public void MoreMemory_ThanTheSwapLimitAllows_AsksForTheSwapToo()
	{
		IReadOnlyDictionary<string, string> raised = (DockerUpdateSpec.From(Limited) with { Memory = "2g" }).Problems();
		IReadOnlyDictionary<string, string> both = (DockerUpdateSpec.From(Limited) with { Memory = "2g", MemorySwap = "1500m" }).Problems();

		Assert.Equal(
			"The swap limit, 1g, counts memory and swap together and is below the new memory limit. Raise it to at least 2g, or -1 for no swap limit.",
			raised[DockerUpdateSpec.SwapField]);
		Assert.Equal("The swap limit counts memory and swap together, so it cannot be below the memory limit, 2g.", both[DockerUpdateSpec.SwapField]);
		Assert.Empty((DockerUpdateSpec.From(Limited) with { Memory = "2g", MemorySwap = "4g" }).Problems());
	}

	[Fact]
	public void Swap_WithoutAMemoryLimit_IsRefused()
	{
		IReadOnlyDictionary<string, string> problems = (DockerUpdateSpec.From(DockerContainerLimits.None) with { MemorySwap = "1g" }).Problems();

		Assert.Equal("A swap limit needs a memory limit: set memory as well.", problems[DockerUpdateSpec.SwapField]);
	}

	[Fact]
	public void TheHostsCpus_AndTheOlderQuota_AreDockersOwnRefusals()
	{
		Assert.Contains("This host has 4 CPUs", (DockerUpdateSpec.From(Limited) with { Cpus = "6" }).Problems(hostCpus: 4)[DockerUpdateSpec.CpusField], StringComparison.Ordinal);

		DockerContainerLimits quota = DockerContainerLimits.None with { CpuQuota = 50_000, CpuPeriod = 100_000 };
		Assert.Equal(0.5m, quota.QuotaCpus);
		Assert.Contains("--cpu-quota", (DockerUpdateSpec.From(quota) with { Cpus = "1" }).Problems()[DockerUpdateSpec.CpusField], StringComparison.Ordinal);
	}

	[Fact]
	public void AContainerStartedWithRm_GetsNoRestartPolicy()
	{
		DockerContainerLimits removed = DockerContainerLimits.None with { AutoRemove = true };

		Assert.Contains("--rm", (DockerUpdateSpec.From(removed) with { Restart = DockerRestartPolicy.Always }).Problems()[DockerUpdateSpec.RestartField], StringComparison.Ordinal);
		Assert.Empty((DockerUpdateSpec.From(removed) with { Memory = "64m" }).Problems());
	}

	[Fact]
	public void Retries_StayWithinWhatAPolicyMeans()
	{
		Assert.Contains("Retries run from 0", (DockerUpdateSpec.From(Limited) with { MaxRetries = 5000 }).Problems()[DockerUpdateSpec.RestartField], StringComparison.Ordinal);
		Assert.Equal("on-failure", (DockerUpdateSpec.From(Limited) with { MaxRetries = 0 }).RestartWord());
	}

	[Fact]
	public void Sizes_AreShownTheWayDockerReadsThemBack()
	{
		Assert.Equal("512m", DockerContainerLimits.SizeText(512L << 20));
		Assert.Equal("2g", DockerContainerLimits.SizeText(2L << 30));
		Assert.Equal("1536m", DockerContainerLimits.SizeText(1536L << 20));
		Assert.Equal("1000", DockerContainerLimits.SizeText(1000));
		Assert.Equal("0.25", DockerContainerLimits.CpusText(0.25m));
		Assert.Equal("2", DockerContainerLimits.CpusText(2m));
	}

	[Fact]
	public void AName_ThatBreaksDockersRule_NeverReachesTheLine() =>
		Assert.Throws<ArgumentException>(() => (DockerUpdateSpec.From(Limited) with { Memory = "1g" }).Arguments("-rf"));
}
