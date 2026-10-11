namespace Mokaterm.Modules.Ssh.Admin.Storage;

/// <summary>The disks page: block devices, file system usage and mounts.</summary>
internal sealed record DiskSnapshot
{
	public IReadOnlyList<BlockDevice> Devices { get; init; } = [];

	/// <summary>True when the devices came from <c>/proc/partitions</c> because lsblk is missing; then only names and sizes are known.</summary>
	public bool DevicesFromPartitions { get; init; }

	public IReadOnlyList<FileSystemUsage> Usage { get; init; } = [];

	public IReadOnlyList<MountEntry> Mounts { get; init; } = [];
}
