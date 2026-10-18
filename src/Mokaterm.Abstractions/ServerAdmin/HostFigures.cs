using Mokaterm.Abstractions.Fleet;

namespace Mokaterm.Abstractions.ServerAdmin;

/// <summary>
/// One read of a machine's live figures. Every field is optional: a machine that does not report a figure leaves it
/// empty, and CPU use is a difference between two reads, so the first read has none.
/// </summary>
public sealed record HostFigures
{
	/// <summary>When the read came back, by this computer's clock.</summary>
	public DateTimeOffset ReadAt { get; init; }

	/// <summary>Share of all CPU time spent working since the read before, 0 to 100.</summary>
	public double? CpuPercent { get; init; }

	/// <summary>The load average over one minute.</summary>
	public double? Load1 { get; init; }

	public double? Load5 { get; init; }

	public double? Load15 { get; init; }

	/// <summary>Memory in use, which is what the kernel says is not available to start something new.</summary>
	public long? MemoryUsedBytes { get; init; }

	public long? MemoryTotalBytes { get; init; }

	public long? SwapUsedBytes { get; init; }

	public long? SwapTotalBytes { get; init; }

	/// <summary>The file system mounted at <c>/</c>.</summary>
	public HostDiskUsage? RootDisk { get; init; }

	public TimeSpan? Uptime { get; init; }

	/// <summary>Memory in use as a share of all of it, 0 to 100.</summary>
	public double? MemoryPercent => Share(MemoryUsedBytes, MemoryTotalBytes);

	/// <summary>Swap in use as a share of all of it, 0 to 100; null for a machine without swap.</summary>
	public double? SwapPercent => Share(SwapUsedBytes, SwapTotalBytes);

	private static double? Share(long? used, long? total) =>
		used is { } part && total is { } whole && whole > 0 ? Math.Clamp(part * 100.0 / whole, 0, 100) : null;
}
