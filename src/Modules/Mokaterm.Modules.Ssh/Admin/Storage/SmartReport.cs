namespace Mokaterm.Modules.Ssh.Admin.Storage;

/// <summary>What smartctl said about one disk: its overall health, who it is, and the figures that wear out.</summary>
internal sealed record SmartReport
{
	/// <summary>The disk's own verdict: true for PASSED (or OK on SCSI), false for FAILED, null when it gave none.</summary>
	public bool? Passed { get; init; }

	/// <summary>The verdict as smartctl printed it, or empty.</summary>
	public string HealthText { get; init; } = "";

	/// <summary>ATA, NVMe or SCSI, when smartctl said.</summary>
	public string Protocol { get; init; } = "";

	public string Model { get; init; } = "";

	public string Serial { get; init; } = "";

	public string Firmware { get; init; } = "";

	public long? CapacityBytes { get; init; }

	/// <summary>Revolutions per minute; 0 for a solid state disk.</summary>
	public int? RotationRpm { get; init; }

	/// <summary>Whether the disk has SMART at all, and whether it is turned on.</summary>
	public bool? SmartAvailable { get; init; }

	public bool? SmartEnabled { get; init; }

	public int? TemperatureCelsius { get; init; }

	public long? PowerOnHours { get; init; }

	public long? PowerCycles { get; init; }

	/// <summary>The ATA attribute table; empty for NVMe and SCSI disks.</summary>
	public IReadOnlyList<SmartAttribute> Attributes { get; init; } = [];

	/// <summary>NVMe's health log, label and value, in the order smartctl prints them; empty for other disks.</summary>
	public IReadOnlyList<(string Label, string Value)> NvmeHealth { get; init; } = [];

	/// <summary>What smartctl said went wrong, such as a device type it could not tell.</summary>
	public IReadOnlyList<string> Messages { get; init; } = [];

	/// <summary>smartctl's exit status, a bit mask: 2 means it could not open the disk, 8 that the disk is failing.</summary>
	public int? ExitStatus { get; init; }

	/// <summary>The version that answered, such as <c>7.4</c>.</summary>
	public string Version { get; init; } = "";

	/// <summary>Anything worth a reader's eye: a failed verdict, a failed attribute, a sector counter above zero, an NVMe warning.</summary>
	public bool NeedsAttention => Passed == false || Attributes.Any(attribute => attribute.Failed || attribute.Worrying) || NvmeWarning;

	/// <summary>NVMe's critical warning bits are set, or its spare space is at or below the drive's own threshold.</summary>
	public bool NvmeWarning { get; init; }

	/// <summary>Why the report holds no health at all, in a sentence, or null when it has one.</summary>
	public string? Problem => Passed is not null || Attributes.Count > 0 || NvmeHealth.Count > 0
		? null
		: SmartAvailable == false
			? "The disk says it has no SMART, which is usual for USB enclosures, SD cards and virtual disks."
			: SmartEnabled == false
				? "SMART is turned off on this disk (smartctl -s on turns it on)."
				: Messages.Count > 0 && Messages[0].Length > 0
					? $"smartctl could not read the disk: {Messages[0]}"
					: "smartctl gave no health for this disk.";
}
