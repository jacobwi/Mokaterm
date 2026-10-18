using Mokaterm.Modules.Ssh.Admin.Storage;

namespace Mokaterm.Modules.Ssh.Admin.Monitoring;

/// <summary>
/// The machine's counters at one moment, read in one go. Memory and load mean something on their own; CPU, network
/// and disk figures only as the difference to an earlier sample (<see cref="SystemRates.Between"/>).
/// </summary>
internal sealed record SystemSample
{
	/// <summary>Seconds since boot from <c>/proc/uptime</c>: the server's own clock between two samples, so a slow round trip does not skew a rate.</summary>
	public double? UptimeSeconds { get; init; }

	public CpuTimes? Cpu { get; init; }

	public LoadAverage? Load { get; init; }

	public MemoryInfo Memory { get; init; } = MemoryInfo.Unknown;

	/// <summary>Every interface but loopback.</summary>
	public IReadOnlyList<InterfaceCounters> Interfaces { get; init; } = [];

	/// <summary>Whole disks only: no partitions, loop images, RAM disks or device-mapper volumes, which would count the same bytes twice.</summary>
	public IReadOnlyList<DiskCounters> Disks { get; init; } = [];

	/// <summary>The file system mounted at <c>/</c>, from <c>df -P -k /</c>; null when df did not answer.</summary>
	public FileSystemUsage? RootDisk { get; init; }

	/// <summary>
	/// Nothing came back from <c>/proc</c>: no uptime, no CPU times and no memory. That is a machine that is not Linux, and
	/// asking it again will not change the answer.
	/// </summary>
	public bool ReadsNoProc => UptimeSeconds is null && Cpu is null && Memory.TotalKb is null;
}
