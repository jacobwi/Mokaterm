namespace Mokaterm.Modules.Ssh.Admin.Storage;

/// <summary>A disk, partition, LVM volume or other block device, with what sits on top of it.</summary>
internal sealed record BlockDevice
{
	public required string Name { get; init; }

	/// <summary><c>disk</c>, <c>part</c>, <c>lvm</c>, <c>crypt</c>, <c>rom</c>, <c>loop</c>.</summary>
	public string Type { get; init; } = "";

	public long? SizeBytes { get; init; }

	public string FileSystem { get; init; } = "";

	public string MountPoint { get; init; } = "";

	public string Label { get; init; } = "";

	public string Model { get; init; } = "";

	public bool ReadOnly { get; init; }

	public bool Removable { get; init; }

	public IReadOnlyList<BlockDevice> Children { get; init; } = [];
}
