using System.Text;
using Mokaterm.Modules.Ssh.Admin;
using Mokaterm.Modules.Ssh.Admin.Monitoring;
using Mokaterm.Modules.Ssh.Admin.Overview;
using Mokaterm.Modules.Ssh.Shell;

namespace Mokaterm.Modules.Ssh.Tests.Admin;

/// <summary>The overview's live figures: reading /proc, working out rates between two samples, and keeping the history.</summary>
public sealed class MonitorTests
{
	private static readonly TimeSpan ShellTimeout = TimeSpan.FromSeconds(30);

	[Fact]
	public void Sample_ReadsEveryFile_AndKeepsWholeDisksOnly()
	{
		SystemSample sample = MonitorCommands.Parse(MonitorFixtures.UbuntuFirst);

		Assert.Equal(350735.47, sample.UptimeSeconds);
		Assert.Equal(new CpuTimes(2255, 34, 2290, 22625563, 6290, 127, 456, 0), sample.Cpu);
		Assert.Equal(new LoadAverage(0.42, 0.31, 0.27), sample.Load);
		Assert.Equal(5122000, sample.Memory.AvailableKb);
		Assert.Equal(["eth0", "docker0", "veth3f1a2b9"], sample.Interfaces.Select(item => item.Name));
		Assert.Equal([true, false, false], sample.Interfaces.Select(item => item.IsDevice));
		Assert.Equal(1215645, sample.Interfaces[0].ReceivedBytes);
		Assert.Equal(1782404, sample.Interfaces[0].SentBytes);

		// The loop image and the LVM volume on top of vda would count vda's bytes again; vda1 is a partition.
		DiskCounters disk = Assert.Single(sample.Disks);
		Assert.Equal(new DiskCounters("vda", 6158522, 9946376), disk);
	}

	[Fact]
	public void Rates_BetweenTwoSamples()
	{
		SystemRates? rates = SystemRates.Between(MonitorCommands.Parse(MonitorFixtures.UbuntuFirst), MonitorCommands.Parse(MonitorFixtures.UbuntuSecond));

		Assert.NotNull(rates);
		Assert.Equal(2.0, rates.Seconds!.Value, 3);
		Assert.Equal(25.0, rates.CpuPercent!.Value, 6);
		Assert.Equal(2.0, rates.IoWaitPercent!.Value, 6);

		// Only eth0 is a device: the bridge and the veth carry the containers' share of the same bytes again.
		Assert.Equal(100_000, rates.ReceivedPerSecond!.Value, 3);
		Assert.Equal(50_000, rates.SentPerSecond!.Value, 3);
		Assert.Equal(["eth0", "docker0", "veth3f1a2b9"], rates.Interfaces.Select(item => item.Name));
		Assert.Equal(50_000, rates.Interfaces[1].ReceivedPerSecond, 3);

		Assert.Equal(1_048_576, rates.ReadPerSecond!.Value, 3);
		Assert.Equal(2_097_152, rates.WrittenPerSecond!.Value, 3);
		Assert.Equal("vda", Assert.Single(rates.Disks).Name);
	}

	[Fact]
	public void Rates_InAContainer_CountEveryInterfaceButLoopback()
	{
		SystemSample first = MonitorCommands.Parse(MonitorFixtures.AlpineContainer);
		SystemSample second = first with
		{
			UptimeSeconds = first.UptimeSeconds + 4,
			Interfaces = [new InterfaceCounters("eth0", 18342 + 4000, 2241 + 400, IsDevice: false)],
		};

		SystemRates rates = SystemRates.Between(first, second)!;

		Assert.Equal("nvme0n1", Assert.Single(first.Disks).Name);
		Assert.Equal(1000, rates.ReceivedPerSecond!.Value, 3);
		Assert.Equal(100, rates.SentPerSecond!.Value, 3);
		Assert.Null(rates.CpuPercent);
	}

	[Fact]
	public void Rates_LeaveOutACounterThatWentBack_AndRefuseASampleThatIsNotLater()
	{
		SystemSample first = MonitorCommands.Parse(MonitorFixtures.UbuntuFirst);
		SystemSample second = MonitorCommands.Parse(MonitorFixtures.UbuntuSecond);

		// docker0 came up again in between, so its counters started over.
		SystemSample reset = second with { Interfaces = [.. second.Interfaces.Select(item => item.Name == "docker0" ? item with { ReceivedBytes = 10 } : item)] };
		SystemRates rates = SystemRates.Between(first, reset)!;
		Assert.DoesNotContain(rates.Interfaces, item => item.Name == "docker0");
		Assert.Equal(100_000, rates.ReceivedPerSecond!.Value, 3);

		Assert.Null(SystemRates.Between(second, first));
		Assert.Null(SystemRates.Between(first, first));
	}

	[Theory]
	[InlineData("cpu  1393280 32966 572056 13343292 6130 0 17875", 13343292L + 1393280 + 32966 + 572056 + 6130 + 17875)]
	[InlineData("cpu  1 2 3 4", 10L)]
	[InlineData("cpu  2255 34 2290 22625563 6290 127 456 7 900 0", 22637015L + 7)]
	public void CpuTimes_OldAndNewKernels_LeaveGuestTimeOut(string line, long total) =>
		Assert.Equal(total, CpuTimes.Parse(line)!.Value.Total);

	[Theory]
	[InlineData("cpu0 1 2 3 4 5")]
	[InlineData("intr 1 2 3 4 5")]
	[InlineData("cpu  1 2 x 4")]
	[InlineData("")]
	public void CpuTimes_RefuseOtherLines(string line) => Assert.Null(CpuTimes.Parse(line));

	[Fact]
	public void NetDev_OldKernels_PutTheCountRightAfterTheColon()
	{
		IReadOnlyList<InterfaceCounters> interfaces = MonitorCommands.ParseNetDev(
			"Inter-|   Receive\n face |bytes\n  eth0:1215645    2751    0    0    0     0          0         0  1782404    4324    0    0    0   427       0          0\n",
			new HashSet<string>(["eth0"], StringComparer.Ordinal));

		Assert.Equal(new InterfaceCounters("eth0", 1215645, 1782404, true), Assert.Single(interfaces));
	}

	[Fact]
	public void DiskStats_OldPartitionLines_AndNoSysfs()
	{
		const string Stats = """
			   8    0 sda 4136 1252 106066 46220 6420 4361 86488 104876 0 46592 151096
			   8    1 sda1 286 2286 16 128
			   3    0 hda 10 0 80 1 0 0 0 0 0 1 1
			 179    0 mmcblk0 2000 10 160000 300 400 20 32000 900 0 800 1200
			 179    1 mmcblk0p1 100 0 800 10 0 0 0 0 0 10 10
			 259    0 nvme0n1 41211 1201 3302112 9911 82010 9011 6612010 41020 0 40120 51031
			 259    1 nvme0n1p2 41000 1201 3300000 9900 82000 9011 6612000 41000 0 40100 51000
			   1    0 ram0 0 0 0 0 0 0 0 0 0 0 0
			   9    0 md0 100 0 800 0 100 0 800 0 0 0 0
			""";

		IReadOnlyList<DiskCounters> disks = MonitorCommands.ParseDiskStats(Stats, []);

		Assert.Equal(["sda", "hda", "mmcblk0", "nvme0n1"], disks.Select(disk => disk.Name));
		Assert.Equal(new DiskCounters("sda", 106066, 86488), disks[0]);
	}

	[Fact]
	public void DiskStats_SysfsNamesWithASlash()
	{
		IReadOnlyList<DiskCounters> disks = MonitorCommands.ParseDiskStats(" 104 0 cciss/c0d0 10 0 80 1 5 0 40 0 0 1 1\n 104 1 cciss/c0d0p1 10 0 80 1 5 0 40 0 0 1 1\n", ["cciss!c0d0"]);

		Assert.Equal("cciss/c0d0", Assert.Single(disks).Name);
	}

	[Fact]
	public void Memory_WithoutMemAvailable_AddsFreeBuffersAndCache()
	{
		MemoryInfo memory = MemoryInfo.Parse("MemTotal: 1000 kB\nMemFree: 100 kB\nBuffers: 50 kB\nCached: 250 kB\nSwapTotal: 0 kB\nSwapFree: 0 kB\n");

		Assert.Equal(400, memory.AvailableKb);
		Assert.Equal(600, memory.UsedKb);
		Assert.Equal(60, memory.UsedPercent);
		Assert.Null(memory.SwapUsedPercent);
		Assert.Null(MemoryInfo.Parse("").UsedPercent);
	}

	[Fact]
	public void History_StartsRatesWithTheSecondSample_AndDropsALateOne()
	{
		MonitorHistory history = new();
		SystemSample first = MonitorCommands.Parse(MonitorFixtures.UbuntuFirst);
		SystemSample second = MonitorCommands.Parse(MonitorFixtures.UbuntuSecond);

		Assert.True(history.Add(first));
		Assert.Null(history.Rates);
		Assert.Single(history.Memory);
		Assert.Empty(history.Cpu);

		Assert.True(history.Add(second));
		Assert.Equal(25, history.Cpu.Latest!.Value, 6);
		Assert.Equal(100_000, history.Received.Latest!.Value, 3);
		Assert.Equal(2, history.Memory.Count);

		// A read that came back after a newer one is older by the server's clock.
		Assert.False(history.Add(first));
		Assert.Same(second, history.Latest);
		Assert.Single(history.Cpu);
	}

	[Fact]
	public void History_KeepsTheNewestSixty()
	{
		MonitorHistory history = new();
		SystemSample sample = MonitorCommands.Parse(MonitorFixtures.UbuntuFirst);
		for (int index = 0; index < MonitorHistory.Capacity + 25; index++)
		{
			CpuTimes cpu = sample.Cpu!.Value;
			history.Add(sample with
			{
				UptimeSeconds = sample.UptimeSeconds + (index * 2),
				Cpu = cpu with { User = cpu.User + (index * index), Idle = cpu.Idle + (index * 400) },
			});
		}

		Assert.Equal(MonitorHistory.Capacity, history.Cpu.Count);
		Assert.Equal(MonitorHistory.Capacity, history.Memory.Count);
		Assert.True(history.Cpu[^1] > history.Cpu[0]);
	}

	[Fact]
	public void Overview_StartsWithASample()
	{
		OverviewSnapshot snapshot = OverviewCommands.Parse(MonitorFixtures.UbuntuFirst + "\n@@mt:cores\n4\n");

		Assert.Equal(TimeSpan.FromSeconds(350735), snapshot.Uptime);
		Assert.Equal(0.42, snapshot.Load1);
		Assert.Equal(4, snapshot.Cores);
		Assert.Equal("vda", Assert.Single(snapshot.Sample.Disks).Name);
		Assert.Contains("/proc/diskstats", OverviewCommands.Read().Body, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Sample_RunsInALocalShell_AndParses()
	{
		if (PosixShellLocator.Find() is not { } shell)
		{
			Assert.Skip("No POSIX sh is available on this machine.");
			return;
		}

		AdminScript script = MonitorCommands.Sample();
		(int exitCode, byte[] output, string error) = await PosixShellLocator.RunAsync(shell, PosixShellLocator.UnixPathPrefix + AdminScriptText.Compose(script), []).WaitAsync(ShellTimeout, TestContext.Current.CancellationToken);

		Assert.True(exitCode == 0, error);
		Assert.True(RemoteOutput.TryGetPayload(output, out ReadOnlyMemory<byte> payload));
		SystemSample sample = MonitorCommands.Parse(Encoding.UTF8.GetString(payload.Span));

		// Git for Windows has no /proc/net/dev or /sys/block, but every Linux and Cygwin has these.
		Assert.NotNull(sample.UptimeSeconds);
		Assert.NotNull(sample.Cpu);
		Assert.NotNull(sample.Memory.TotalKb);
	}
}
