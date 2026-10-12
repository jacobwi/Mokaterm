using Mokaterm.Modules.Ssh.Docker;

namespace Mokaterm.Modules.Ssh.Tests.Docker;

/// <summary>The figures the stats page sorts by, and the sample lines the page and the container's tab share.</summary>
public sealed class DockerStatsHistoryTests
{
	[Fact]
	public void Stats_ReadTheNumbersBehindDockersText()
	{
		DockerContainerStats web = DockerContainerStats.Parse(DockerFixtures.Stats)[0];

		Assert.Equal(10_695_475, web.MemoryBytes);
		Assert.Equal(1_200, web.NetworkBytes);
		Assert.Equal(16_400_000, web.BlockBytes);
		Assert.Equal(5, web.PidCount);
		Assert.Equal(0.13, web.CpuShare(4)!.Value, 6);
	}

	[Fact]
	public void Stats_AStoppedContainersDashes_ReadAsNothing()
	{
		DockerContainerStats stopped = DockerContainerStats.Parse(DockerFixtures.Stats)[1];

		Assert.Null(stopped.MemoryBytes);
		Assert.Null(stopped.NetworkBytes);
		Assert.Null(stopped.PidCount);
		Assert.Null(stopped.CpuShare(4));
	}

	[Fact]
	public void History_KeepsTheNewestSamples_PerContainer()
	{
		DockerStatsHistory history = new();
		for (int sample = 0; sample < DockerStatsHistory.Length + 5; sample++)
		{
			history.Record([Sample("web", sample), Sample("db", sample * 2)]);
		}

		Assert.Equal(DockerStatsHistory.Length, history.Cpu("web").Count);
		Assert.Equal(DockerStatsHistory.Length + 4, history.Cpu("web")[^1]);
		Assert.Equal((DockerStatsHistory.Length + 4) * 2, history.Cpu("db")[^1]);
		Assert.Equal(50, history.Memory("web")[^1]);
	}

	[Fact]
	public void History_DropsTheLineOfAContainerThatStopped()
	{
		DockerStatsHistory history = new();
		history.Record([Sample("web", 1), Sample("db", 2)]);
		history.Record([Sample("web", 3)]);

		Assert.Equal([1d, 3d], history.Cpu("web"));
		Assert.Empty(history.Cpu("db"));
		Assert.Empty(history.Memory("unknown"));
	}

	private static DockerContainerStats Sample(string id, double cpu) => new()
	{
		Id = id,
		Name = id,
		Cpu = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{cpu}%"),
		MemoryPercent = "50%",
	};
}
