namespace Mokaterm.Modules.Ssh.Admin.Monitoring;

/// <summary>
/// Sectors one whole disk read and wrote since boot, from <c>/proc/diskstats</c>. The kernel counts in 512-byte sectors
/// there whatever the disk's own sector size is.
/// </summary>
internal sealed record DiskCounters(string Name, long SectorsRead, long SectorsWritten)
{
	public const int SectorBytes = 512;
}
