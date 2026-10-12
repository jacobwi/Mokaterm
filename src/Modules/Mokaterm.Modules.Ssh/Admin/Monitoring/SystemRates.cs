namespace Mokaterm.Modules.Ssh.Admin.Monitoring;

/// <summary>What happened between two samples: CPU shares, and network and disk bytes per second.</summary>
internal sealed record SystemRates
{
	/// <summary>Seconds between the samples by the server's uptime, or null when either lacked one.</summary>
	public double? Seconds { get; init; }

	/// <summary>Share of all CPU time spent working (not idle, not waiting on I/O), 0 to 100.</summary>
	public double? CpuPercent { get; init; }

	/// <summary>Share of all CPU time spent idle with I/O outstanding, 0 to 100: high means the disks hold the machine back.</summary>
	public double? IoWaitPercent { get; init; }

	public double? ReceivedPerSecond { get; init; }

	public double? SentPerSecond { get; init; }

	public double? ReadPerSecond { get; init; }

	public double? WrittenPerSecond { get; init; }

	/// <summary>Every interface present in both samples, busiest first.</summary>
	public IReadOnlyList<InterfaceRate> Interfaces { get; init; } = [];

	/// <summary>Every whole disk present in both samples, busiest first.</summary>
	public IReadOnlyList<DiskRate> Disks { get; init; } = [];

	/// <summary>
	/// The rates from <paramref name="earlier"/> to <paramref name="later"/>, or null when the second is not later (the
	/// machine rebooted, or a slow read came back after a newer one). A counter that went backwards (an interface that
	/// came up again, a 32-bit counter that wrapped) leaves out only that interface or disk.
	/// </summary>
	public static SystemRates? Between(SystemSample earlier, SystemSample later)
	{
		ArgumentNullException.ThrowIfNull(earlier);
		ArgumentNullException.ThrowIfNull(later);
		double? seconds = later.UptimeSeconds - earlier.UptimeSeconds;
		if (seconds is <= 0)
		{
			return null;
		}

		(double? cpu, double? ioWait) = CpuShares(earlier.Cpu, later.Cpu);
		List<InterfaceRate> interfaces = seconds is { } span ? InterfaceRates(earlier, later, span) : [];
		List<DiskRate> disks = seconds is { } elapsed ? DiskRates(earlier, later, elapsed) : [];

		// Bridges and veth pairs carry what a device also carries; inside a container no interface is a device.
		List<InterfaceRate> counted = interfaces.Any(rate => rate.IsDevice) ? [.. interfaces.Where(rate => rate.IsDevice)] : interfaces;
		return new SystemRates
		{
			Seconds = seconds,
			CpuPercent = cpu,
			IoWaitPercent = ioWait,
			ReceivedPerSecond = seconds is null ? null : counted.Sum(rate => rate.ReceivedPerSecond),
			SentPerSecond = seconds is null ? null : counted.Sum(rate => rate.SentPerSecond),
			ReadPerSecond = seconds is null ? null : disks.Sum(rate => rate.ReadPerSecond),
			WrittenPerSecond = seconds is null ? null : disks.Sum(rate => rate.WrittenPerSecond),
			Interfaces = [.. interfaces.OrderByDescending(rate => rate.ReceivedPerSecond + rate.SentPerSecond).ThenBy(rate => rate.Name, StringComparer.Ordinal)],
			Disks = [.. disks.OrderByDescending(rate => rate.ReadPerSecond + rate.WrittenPerSecond).ThenBy(rate => rate.Name, StringComparer.Ordinal)],
		};
	}

	private static (double? Cpu, double? IoWait) CpuShares(CpuTimes? earlier, CpuTimes? later)
	{
		if (earlier is not { } before || later is not { } after)
		{
			return (null, null);
		}

		long total = after.Total - before.Total;
		long idle = after.Idle - before.Idle;
		long ioWait = after.IoWait - before.IoWait;
		if (total <= 0 || idle < 0)
		{
			return (null, null);
		}

		// iowait is an estimate the kernel may lower again on a busy machine; a negative step reads as none.
		ioWait = Math.Max(0, ioWait);
		double busy = Math.Clamp((total - idle - ioWait) * 100.0 / total, 0, 100);
		return (busy, Math.Clamp(ioWait * 100.0 / total, 0, 100));
	}

	private static List<InterfaceRate> InterfaceRates(SystemSample earlier, SystemSample later, double seconds)
	{
		Dictionary<string, InterfaceCounters> before = earlier.Interfaces.GroupBy(item => item.Name, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
		List<InterfaceRate> rates = [];
		foreach (InterfaceCounters after in later.Interfaces)
		{
			if (before.TryGetValue(after.Name, out InterfaceCounters? old)
				&& after.ReceivedBytes >= old.ReceivedBytes
				&& after.SentBytes >= old.SentBytes)
			{
				rates.Add(new InterfaceRate(after.Name, (after.ReceivedBytes - old.ReceivedBytes) / seconds, (after.SentBytes - old.SentBytes) / seconds, after.IsDevice));
			}
		}

		return rates;
	}

	private static List<DiskRate> DiskRates(SystemSample earlier, SystemSample later, double seconds)
	{
		Dictionary<string, DiskCounters> before = earlier.Disks.GroupBy(item => item.Name, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
		List<DiskRate> rates = [];
		foreach (DiskCounters after in later.Disks)
		{
			if (before.TryGetValue(after.Name, out DiskCounters? old)
				&& after.SectorsRead >= old.SectorsRead
				&& after.SectorsWritten >= old.SectorsWritten)
			{
				rates.Add(new DiskRate(
					after.Name,
					(after.SectorsRead - old.SectorsRead) * (double)DiskCounters.SectorBytes / seconds,
					(after.SectorsWritten - old.SectorsWritten) * (double)DiskCounters.SectorBytes / seconds));
			}
		}

		return rates;
	}
}
