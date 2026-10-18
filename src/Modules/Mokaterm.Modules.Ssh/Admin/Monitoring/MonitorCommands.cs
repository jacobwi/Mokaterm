using System.Globalization;
using System.Text.RegularExpressions;
using Mokaterm.Modules.Ssh.Admin.Storage;

namespace Mokaterm.Modules.Ssh.Admin.Monitoring;

/// <summary>
/// The overview's live sample: one script that only reads small files under <c>/proc</c> and lists two folders under
/// <c>/sys</c>, so it is cheap enough to run every couple of seconds. Every difference is worked out here, not there.
/// </summary>
internal static partial class MonitorCommands
{
	/// <summary>How often the overview samples while it is shown.</summary>
	public static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

	/// <summary>A sample that takes longer than this is stuck, and the next one will try again.</summary>
	public static readonly TimeSpan SampleTimeout = TimeSpan.FromSeconds(10);

	/// <summary>Block devices whose bytes are already counted on a whole disk, or are not disks at all.</summary>
	private static readonly string[] NotDisks = ["loop", "ram", "zram", "dm-", "md", "sr", "fd", "nbd"];

	/// <summary>
	/// The lines of a sample, which the overview's first read prints as well so it starts with one. <c>/sys/block</c>
	/// names the whole disks among the diskstats lines, and a <c>device</c> link under <c>/sys/class/net</c> tells a
	/// real (or virtual machine) NIC from a bridge or a veth. df asks about <c>/</c> alone, so a network mount that hangs
	/// elsewhere cannot hold the sample up.
	/// </summary>
	public static IReadOnlyList<string> SampleLines { get; } =
	[
		AdminSections.Start("uptime"),
		"cat /proc/uptime 2>/dev/null",
		AdminSections.Start("stat"),
		"grep '^cpu ' /proc/stat 2>/dev/null",
		AdminSections.Start("loadavg"),
		"cat /proc/loadavg 2>/dev/null",
		AdminSections.Start("meminfo"),
		MemoryInfo.Command,
		AdminSections.Start("netdev"),
		"cat /proc/net/dev 2>/dev/null",
		AdminSections.Start("nics"),
		"ls -d /sys/class/net/*/device 2>/dev/null",
		AdminSections.Start("diskstats"),
		"cat /proc/diskstats 2>/dev/null",
		AdminSections.Start("blocks"),
		"ls /sys/block 2>/dev/null",
		AdminSections.Start("rootfs"),
		"df -P -k / 2>/dev/null",
	];

	public static AdminScript Sample() => new()
	{
		Name = "overview.sample",
		Timeout = SampleTimeout,
		OutputLimitBytes = 512 * 1024,
		Body = string.Join('\n', [.. SampleLines, "exit 0"]),
	};

	public static SystemSample Parse(string output) => Parse(AdminSections.Parse(output));

	public static SystemSample Parse(AdminSections sections)
	{
		ArgumentNullException.ThrowIfNull(sections);
		string[] uptime = (AdminSections.Lines(sections.Get("uptime")).FirstOrDefault() ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
		return new SystemSample
		{
			UptimeSeconds = uptime.Length > 0 && double.TryParse(uptime[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) && seconds >= 0 ? seconds : null,
			Cpu = AdminSections.Lines(sections.Get("stat")).Select(CpuTimes.Parse).FirstOrDefault(times => times is not null),
			Load = LoadAverage.Parse(sections.Get("loadavg")),
			Memory = MemoryInfo.Parse(sections.Get("meminfo")),
			Interfaces = ParseNetDev(sections.Get("netdev"), DeviceInterfaces(sections.Get("nics"))),
			Disks = ParseDiskStats(sections.Get("diskstats"), [.. AdminSections.Lines(sections.Get("blocks")).Select(name => name.Trim())]),
			RootDisk = DiskCommands.ParseDf(sections.Get("rootfs"), []).FirstOrDefault(usage => usage.MountPoint == "/"),
		};
	}

	/// <summary>
	/// <c>/proc/net/dev</c> after its two header lines: <c>eth0: rx_bytes rx_packets ... tx_bytes ...</c>, with the
	/// received bytes first and the sent ones ninth. Old kernels print no space after the colon once the count is
	/// long, so the name ends at the colon rather than at a blank. Loopback is left out.
	/// </summary>
	public static IReadOnlyList<InterfaceCounters> ParseNetDev(string? text, IReadOnlySet<string> devices)
	{
		ArgumentNullException.ThrowIfNull(devices);
		List<InterfaceCounters> interfaces = [];
		foreach (string line in AdminSections.Lines(text))
		{
			int colon = line.IndexOf(':', StringComparison.Ordinal);
			if (colon <= 0)
			{
				continue;
			}

			string name = line[..colon].Trim();
			string[] words = line[(colon + 1)..].Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
			if (name.Length == 0 || name == "lo" || words.Length < 9
				|| !long.TryParse(words[0], NumberStyles.None, CultureInfo.InvariantCulture, out long received)
				|| !long.TryParse(words[8], NumberStyles.None, CultureInfo.InvariantCulture, out long sent))
			{
				continue;
			}

			interfaces.Add(new InterfaceCounters(name, received, sent, devices.Contains(name)));
		}

		return interfaces;
	}

	/// <summary>The interfaces with a <c>device</c> link, from <c>ls -d /sys/class/net/*/device</c>.</summary>
	public static IReadOnlySet<string> DeviceInterfaces(string? text)
	{
		HashSet<string> names = new(StringComparer.Ordinal);
		foreach (string line in AdminSections.Lines(text))
		{
			const string Prefix = "/sys/class/net/";
			const string Suffix = "/device";
			string path = line.Trim();
			if (path.StartsWith(Prefix, StringComparison.Ordinal) && path.EndsWith(Suffix, StringComparison.Ordinal) && path.Length > Prefix.Length + Suffix.Length)
			{
				names.Add(path[Prefix.Length..^Suffix.Length]);
			}
		}

		return names;
	}

	/// <summary>
	/// <c>/proc/diskstats</c>: <c>major minor name reads merged sectors_read ms writes merged sectors_written ...</c>.
	/// Kernels before 2.6.25 printed four counters for a partition (<c>reads sectors_read writes sectors_written</c>),
	/// which are read too. Only whole disks are kept: those <c>/sys/block</c> lists, or when it listed nothing (no
	/// sysfs) those whose name does not look like a partition.
	/// </summary>
	public static IReadOnlyList<DiskCounters> ParseDiskStats(string? text, IReadOnlyCollection<string> blockDevices)
	{
		ArgumentNullException.ThrowIfNull(blockDevices);

		// sysfs cannot hold a slash in a name, so cciss/c0d0 is listed there as cciss!c0d0.
		HashSet<string> whole = new(blockDevices.Select(name => name.Replace('!', '/')), StringComparer.Ordinal);
		List<DiskCounters> disks = [];
		foreach (string line in AdminSections.Lines(text))
		{
			string[] words = line.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
			(int readAt, int writtenAt) = words.Length switch
			{
				>= 10 => (5, 9),
				7 => (4, 6),
				_ => (-1, -1),
			};

			if (readAt < 0)
			{
				continue;
			}

			string name = words[2];
			if (!IsWholeDisk(name, whole)
				|| !long.TryParse(words[readAt], NumberStyles.None, CultureInfo.InvariantCulture, out long read)
				|| !long.TryParse(words[writtenAt], NumberStyles.None, CultureInfo.InvariantCulture, out long written))
			{
				continue;
			}

			disks.Add(new DiskCounters(name, read, written));
		}

		return disks;
	}

	/// <summary>A disk whose bytes count once: not a partition, loop image, RAM disk, optical drive or a volume on top of other disks.</summary>
	internal static bool IsWholeDisk(string name, IReadOnlySet<string> blockDevices)
	{
		ArgumentNullException.ThrowIfNull(blockDevices);
		if (NotDisks.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
		{
			return false;
		}

		return blockDevices.Count > 0 ? blockDevices.Contains(name) : !PartitionName().IsMatch(name);
	}

	// sda1, vdb2, xvda1, hdc3, nvme0n1p2, mmcblk0p1: the partition names the kernel gives disks it names itself.
	[GeneratedRegex(@"^((s|v|xv|h)d[a-z]+[0-9]+|(nvme[0-9]+n[0-9]+|mmcblk[0-9]+)p[0-9]+)$", RegexOptions.CultureInvariant)]
	private static partial Regex PartitionName();
}
