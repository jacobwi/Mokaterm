namespace Mokaterm.Modules.Ssh.Admin.Storage;

/// <summary>One line of <c>df -P -k</c>, with the type <c>/proc/mounts</c> gives the mount point.</summary>
internal sealed record FileSystemUsage
{
	public required string Source { get; init; }

	public required string MountPoint { get; init; }

	public string Type { get; init; } = "";

	public long TotalKb { get; init; }

	public long UsedKb { get; init; }

	public long AvailableKb { get; init; }

	/// <summary>What df printed as the capacity, 0 to 100.</summary>
	public int UsedPercent { get; init; }
}
