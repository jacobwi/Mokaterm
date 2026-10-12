namespace Mokaterm.Abstractions.Fleet;

/// <summary>
/// What one machine runs and how it is doing, read in one go for the fleet overview. Every field is optional: a
/// machine that has no <c>/proc</c>, no package manager or no Docker leaves those empty, and so does a part of the
/// read that did not finish in time (<see cref="Notes"/> says which). Nothing here is ever saved: it describes what the
/// machine runs.
/// </summary>
public sealed record HostSummary
{
	/// <summary>The distribution's name and version, such as <c>Ubuntu 24.04.1 LTS</c>, or the kernel's name without one.</summary>
	public string? OperatingSystem { get; init; }

	/// <summary>Kernel, release and architecture, such as <c>Linux 6.8.0-45-generic x86_64</c>.</summary>
	public string? Kernel { get; init; }

	/// <summary>What the machine calls itself, which need not be the address it was reached at.</summary>
	public string? HostName { get; init; }

	public TimeSpan? Uptime { get; init; }

	/// <summary>The load average over one minute.</summary>
	public double? Load1 { get; init; }

	public double? Load5 { get; init; }

	public double? Load15 { get; init; }

	public int? CpuCount { get; init; }

	/// <summary>Memory in use, which is what the kernel says is not available to start something new.</summary>
	public long? MemoryUsedBytes { get; init; }

	public long? MemoryTotalBytes { get; init; }

	/// <summary>The real file system closest to full; kernel, memory and snap file systems do not count.</summary>
	public HostDiskUsage? FullestDisk { get; init; }

	/// <summary>Updates the package manager already knows about. Nothing is fetched to find more.</summary>
	public int? PendingUpdates { get; init; }

	/// <summary>How many of <see cref="PendingUpdates"/> are security updates, where the package manager says so for free.</summary>
	public int? SecurityUpdates { get; init; }

	/// <summary>True when an update is waiting for a restart (a new kernel, or the distribution's own flag).</summary>
	public bool? RebootRequired { get; init; }

	/// <summary>Services the service manager reports as failed. Null where it keeps no such state.</summary>
	public int? FailedServices { get; init; }

	/// <summary>The names behind <see cref="FailedServices"/>, in the order the service manager listed them.</summary>
	public IReadOnlyList<string> FailedServiceNames { get; init; } = [];

	/// <summary>Running containers, when Docker answers this login without sudo.</summary>
	public int? ContainersRunning { get; init; }

	public int? ContainersTotal { get; init; }

	/// <summary>Short sentences about the parts that could not be read, such as one that ran out of time.</summary>
	public IReadOnlyList<string> Notes { get; init; } = [];

	/// <summary>Memory in use as a share of all of it, 0 to 100.</summary>
	public double? MemoryPercent => MemoryUsedBytes is { } used && MemoryTotalBytes is { } total && total > 0
		? Math.Clamp(used * 100.0 / total, 0, 100)
		: null;

	/// <summary>The one-minute load per CPU as a share, 100 being every CPU busy. It goes past 100 on an overloaded machine.</summary>
	public double? LoadPercent => Load1 is { } load && CpuCount is { } cpus && cpus > 0 ? load * 100 / cpus : null;
}
