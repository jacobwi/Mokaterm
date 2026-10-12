namespace Mokaterm.Abstractions.Fleet;

/// <summary>One file system of a machine: where it is mounted, how big it is and how full.</summary>
public sealed record HostDiskUsage
{
	public required string MountPoint { get; init; }

	public long TotalBytes { get; init; }

	public long UsedBytes { get; init; }

	/// <summary>What the machine reports as the share in use, 0 to 100; it counts the space kept for root as used.</summary>
	public int UsedPercent { get; init; }
}
