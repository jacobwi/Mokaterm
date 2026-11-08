using System.Globalization;
using Mokaterm.Modules.Ssh.Docker;

namespace Mokaterm.Modules.Ssh.Tests.Docker;

/// <summary>The figures the stats page sorts by, and the sample lines the page and the container's tab share.</summary>
public sealed class DockerStatsHistoryTests
{
	private static readonly DateTimeOffset Start = new(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);

	[Fact]
	public void Stats_ReadTheNumbersBehindDockersText()
	{
		DockerContainerStats web = DockerContainerStats.Parse(DockerFixtures.Stats)[0];

		Assert.Equal(10_695_475, web.MemoryBytes);
		Assert.Equal(1_200, web.NetworkBytes);
		Assert.Equal(16_400_000, web.BlockBytes);
		Assert.Equal(5, web.PidCount);
		Assert.Equal(0.13, web.CpuShare(4)!.Value, 6);
		Assert.Equal("7.6GiB", web.MemoryLimit);
	}

	[Fact]
	public void Stats_AStoppedContainersDashes_ReadAsNothing()
	{
		DockerContainerStats stopped = DockerContainerStats.Parse(DockerFixtures.Stats)[1];

		Assert.Null(stopped.MemoryBytes);
		Assert.Null(stopped.NetworkBytes);
		Assert.Null(stopped.PidCount);
		Assert.Null(stopped.CpuShare(4));
		Assert.Equal("", stopped.MemoryLimit);
	}

	[Fact]
	public void History_KeepsTheNewestSamples_PerContainer()
	{
		DockerStatsHistory history = new();
		for (int sample = 0; sample < DockerStatsHistory.Length + 5; sample++)
		{
			history.Record([Sample("web", sample), Sample("db", sample * 2)], At(sample));
		}

		Assert.Equal(DockerStatsHistory.Length, history.Cpu("web").Count);
		Assert.Equal(DockerStatsHistory.Length + 4, history.Cpu("web")[^1]);
		Assert.Equal((DockerStatsHistory.Length + 4) * 2, history.Cpu("db")[^1]);
		Assert.Equal(50, history.Memory("web")[^1]);
		Assert.Equal(10 * 1024 * 1024, history.MemoryBytes("web")[^1]);
	}

	[Fact]
	public void History_DropsTheLineOfAContainerThatStopped()
	{
		DockerStatsHistory history = new();
		history.Record([Sample("web", 1), Sample("db", 2)], At(0));
		history.Record([Sample("web", 3)], At(1));

		Assert.Equal([1d, 3d], history.Cpu("web"));
		Assert.Empty(history.Cpu("db"));
		Assert.Empty(history.Memory("unknown"));
		Assert.Equal(["web"], history.Ids);
	}

	[Fact]
	public void Rates_AreMeasuredBetweenTotals_FromTheSecondSample()
	{
		DockerStatsHistory history = new();
		history.Record([Sample("web", 1, network: "0B / 0B", block: "1MB / 0B")], At(0));
		history.Record([Sample("web", 1, network: "30kB / 3kB", block: "1.3MB / 60kB")], At(1));

		Assert.True(double.IsNaN(history.Received("web")[0]), "the first sample has nothing to measure from");
		Assert.Equal(10_000, history.Received("web")[^1]);
		Assert.Equal(1_000, history.Sent("web")[^1]);
		Assert.Equal(100_000, history.Read("web")[^1]);
		Assert.Equal(20_000, history.Written("web")[^1]);
		Assert.Equal(new DockerRates(10_000, 1_000, 100_000, 20_000), history.RatesOf("web"));
	}

	[Fact]
	public void Rates_AreMeasuredOverTheSpan_SoDockersRoundingEvensOut()
	{
		// Docker prints three figures, so a total that grows by 10 kB/s moves only every few samples.
		DockerStatsHistory history = new();
		string[] totals = ["1.00MB", "1.00MB", "1.00MB", "1.09MB", "1.09MB", "1.09MB", "1.18MB", "1.18MB", "1.18MB", "1.27MB", "1.27MB", "1.27MB"];
		for (int sample = 0; sample < totals.Length; sample++)
		{
			history.Record([Sample("web", 1, network: $"{totals[sample]} / 0B")], At(sample));
		}

		// The newest rate spans the last ten samples, 1.27MB less 1.00MB over 30 seconds, rather than the zero between
		// the last two; once the printed total first moved, no rate reads zero again.
		Assert.Equal(270_000 / 30.0, history.Received("web")[^1], 3);
		Assert.Equal(0, history.Received("web")[1]);
		Assert.All(history.Received("web").Skip(3), rate => Assert.True(rate > 0));
	}

	[Fact]
	public void Rates_StartOver_WhenAContainerRestartsAndItsTotalsGoBack()
	{
		DockerStatsHistory history = new();
		history.Record([Sample("web", 1, network: "5MB / 1MB")], At(0));
		history.Record([Sample("web", 1, network: "6MB / 1MB")], At(1));
		history.Record([Sample("web", 1, network: "1kB / 0B")], At(2));
		history.Record([Sample("web", 1, network: "31kB / 0B")], At(3));

		Assert.True(double.IsNaN(history.Received("web")[2]), "a total that went back measures nothing");
		Assert.Equal(10_000, history.Received("web")[3]);
	}

	[Fact]
	public void Sum_AddsLinesUpByTheirNewestSample_AndLeavesEmptySlotsAsGaps()
	{
		double[] total = DockerStatsHistory.Sum([[1, 2, 3], [10, double.NaN], []]);

		Assert.Equal(DockerStatsHistory.Length, total.Length);
		Assert.Equal(3, total[^1]);
		Assert.Equal(12, total[^2]);
		Assert.Equal(1, total[^3]);
		Assert.True(double.IsNaN(total[^4]));
	}

	private static DateTimeOffset At(int sample) => Start + (DockerStatsHistory.SampleInterval * sample);

	private static DockerContainerStats Sample(string id, double cpu, string network = "", string block = "") => new()
	{
		Id = id,
		Name = id,
		Cpu = string.Create(CultureInfo.InvariantCulture, $"{cpu}%"),
		MemoryPercent = "50%",
		Memory = "10MiB / 20MiB",
		NetworkIo = network,
		BlockIo = block,
	};
}
