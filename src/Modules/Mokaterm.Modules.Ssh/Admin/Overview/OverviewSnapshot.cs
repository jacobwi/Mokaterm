using Mokaterm.Modules.Ssh.Admin.Monitoring;
using Mokaterm.Modules.Ssh.Admin.Storage;

namespace Mokaterm.Modules.Ssh.Admin.Overview;

/// <summary>The overview's numbers, read from <c>/proc</c> and df in one go.</summary>
internal sealed record OverviewSnapshot
{
	/// <summary>The live sample taken with the read, which the overview's history starts from.</summary>
	public SystemSample Sample { get; init; } = new();

	public TimeSpan? Uptime { get; init; }

	/// <summary>Seconds since the epoch on the server when it was read.</summary>
	public long? Now { get; init; }

	public double? Load1 { get; init; }

	public double? Load5 { get; init; }

	public double? Load15 { get; init; }

	public string CpuModel { get; init; } = "";

	public int? Cores { get; init; }

	public long? MemoryTotalKb { get; init; }

	/// <summary>MemAvailable, or free plus buffers and cache on kernels older than 3.14 that do not report it.</summary>
	public long? MemoryAvailableKb { get; init; }

	public long? SwapTotalKb { get; init; }

	public long? SwapFreeKb { get; init; }

	public IReadOnlyList<FileSystemUsage> Disks { get; init; } = [];

	/// <summary>When the machine started, from the server's clock and its uptime.</summary>
	public DateTimeOffset? BootedAt => Now is { } now && Uptime is { } uptime
		? DateTimeOffset.FromUnixTimeSeconds(now) - uptime
		: null;
}
