using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Mokaterm.Modules.Ssh.Admin.Storage;

/// <summary>Reads block devices (<c>lsblk -J</c>), usage (<c>df -P</c>) and mounts. Nothing here changes a disk.</summary>
internal static class DiskCommands
{
	/// <summary>Kernel and virtual file systems: they fill a df listing without saying anything about disk space.</summary>
	private static readonly HashSet<string> PseudoTypes = new(StringComparer.Ordinal)
	{
		"proc", "sysfs", "devtmpfs", "devpts", "tmpfs", "cgroup", "cgroup2", "securityfs", "pstore", "bpf", "debugfs",
		"tracefs", "mqueue", "hugetlbfs", "configfs", "fusectl", "autofs", "binfmt_misc", "efivarfs", "ramfs", "nsfs",
		"rpc_pipefs", "nfsd", "squashfs", "fuse.lxcfs", "fuse.gvfsd-fuse", "fuse.portal", "selinuxfs", "overlay", "devfs",
	};

	public static AdminScript Read() => new()
	{
		Name = "disks.read",
		Body = string.Join('\n',
			"if command -v lsblk >/dev/null 2>&1; then",
			$"\t{AdminSections.Start("lsblk")}",
			"\tlsblk -J -b -o NAME,TYPE,SIZE,FSTYPE,MOUNTPOINT,LABEL,MODEL,RO,RM 2>/dev/null",
			"fi",
			AdminSections.Start("partitions"),
			"cat /proc/partitions 2>/dev/null",
			AdminSections.Start("df"),
			"df -P -k 2>/dev/null",
			AdminSections.Start("mounts"),
			"cat /proc/mounts 2>/dev/null",
			"exit 0"),
	};

	public static DiskSnapshot Parse(string output)
	{
		AdminSections sections = AdminSections.Parse(output);
		IReadOnlyList<MountEntry> mounts = ParseMounts(sections.Get("mounts"));
		IReadOnlyList<BlockDevice>? devices = ParseLsblk(sections.Get("lsblk"));
		return new DiskSnapshot
		{
			Devices = devices ?? ParsePartitions(sections.Get("partitions")),
			DevicesFromPartitions = devices is null,
			Usage = ParseDf(sections.Get("df"), mounts),
			Mounts = mounts,
		};
	}

	/// <summary>
	/// <c>lsblk -J -b</c>. util-linux before 2.33 prints sizes and flags as strings ("500107862016", "0"), later as
	/// numbers and booleans; both read the same here.
	/// </summary>
	public static IReadOnlyList<BlockDevice>? ParseLsblk(string? text)
	{
		string trimmed = text?.Trim() ?? "";
		if (!trimmed.StartsWith('{'))
		{
			return null;
		}

		try
		{
			using JsonDocument document = JsonDocument.Parse(trimmed, new JsonDocumentOptions { MaxDepth = 32 });
			return document.RootElement.TryGetProperty("blockdevices", out JsonElement list) && list.ValueKind == JsonValueKind.Array
				? [.. list.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object).Select(Device)]
				: null;
		}
		catch (JsonException)
		{
			return null;
		}
	}

	/// <summary><c>/proc/partitions</c>: major, minor, size in 1 KiB blocks, name. Only what lsblk would also show is kept.</summary>
	public static IReadOnlyList<BlockDevice> ParsePartitions(string? text)
	{
		List<BlockDevice> devices = [];
		foreach (string line in AdminSections.Lines(text))
		{
			string[] words = line.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
			if (words.Length == 4 && long.TryParse(words[2], NumberStyles.None, CultureInfo.InvariantCulture, out long blocks))
			{
				devices.Add(new BlockDevice { Name = words[3], SizeBytes = blocks * 1024 });
			}
		}

		return devices;
	}

	/// <summary>
	/// <c>df -P -k</c>: source, size, used, available, capacity, mount point. The mount point is the rest of the line,
	/// since it may hold spaces. The type comes from the last mount at that point.
	/// </summary>
	public static IReadOnlyList<FileSystemUsage> ParseDf(string? text, IReadOnlyList<MountEntry> mounts)
	{
		ArgumentNullException.ThrowIfNull(mounts);
		Dictionary<string, string> types = new(StringComparer.Ordinal);
		foreach (MountEntry mount in mounts)
		{
			types[mount.MountPoint] = mount.Type;
		}

		List<FileSystemUsage> usage = [];
		foreach (string line in AdminSections.Lines(text))
		{
			string[] words = line.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
			if (words.Length < 6
				|| !long.TryParse(words[1], NumberStyles.None, CultureInfo.InvariantCulture, out long total)
				|| !long.TryParse(words[2], NumberStyles.None, CultureInfo.InvariantCulture, out long used)
				|| !long.TryParse(words[3], NumberStyles.None, CultureInfo.InvariantCulture, out long available))
			{
				continue;
			}

			string mountPoint = string.Join(' ', words[5..]);
			usage.Add(new FileSystemUsage
			{
				Source = words[0],
				MountPoint = mountPoint,
				Type = types.TryGetValue(mountPoint, out string? type) ? type : "",
				TotalKb = total,
				UsedKb = used,
				AvailableKb = available,
				UsedPercent = int.TryParse(words[4].TrimEnd('%'), NumberStyles.None, CultureInfo.InvariantCulture, out int percent) ? Math.Clamp(percent, 0, 100) : 0,
			});
		}

		return usage;
	}

	/// <summary>
	/// The file systems worth showing for disk space: not a kernel or memory file system and not a snap's loop image.
	/// A container's root is an overlay, and it is the one thing there worth showing.
	/// </summary>
	public static bool IsStorage(FileSystemUsage usage)
	{
		ArgumentNullException.ThrowIfNull(usage);
		if (usage.MountPoint == "/")
		{
			return true;
		}

		return !PseudoTypes.Contains(usage.Type)
			&& !usage.Source.StartsWith("/dev/loop", StringComparison.Ordinal)
			&& usage.Source is not ("tmpfs" or "devtmpfs" or "udev" or "none" or "shm" or "overlay")
			&& usage.TotalKb > 0;
	}

	/// <summary><c>/proc/mounts</c>, where blanks in a path are written as octal escapes (<c>\040</c>).</summary>
	public static IReadOnlyList<MountEntry> ParseMounts(string? text)
	{
		List<MountEntry> mounts = [];
		foreach (string line in AdminSections.Lines(text))
		{
			string[] words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			if (words.Length >= 4)
			{
				mounts.Add(new MountEntry(Unescape(words[0]), Unescape(words[1]), words[2], words[3]));
			}
		}

		return mounts;
	}

	internal static string Unescape(string value)
	{
		if (!value.Contains('\\', StringComparison.Ordinal))
		{
			return value;
		}

		StringBuilder builder = new(value.Length);
		for (int index = 0; index < value.Length; index++)
		{
			if (value[index] == '\\' && index + 3 < value.Length
				&& value[index + 1] is >= '0' and <= '7' && value[index + 2] is >= '0' and <= '7' && value[index + 3] is >= '0' and <= '7')
			{
				builder.Append((char)(((value[index + 1] - '0') * 64) + ((value[index + 2] - '0') * 8) + (value[index + 3] - '0')));
				index += 3;
			}
			else
			{
				builder.Append(value[index]);
			}
		}

		return builder.ToString();
	}

	private static BlockDevice Device(JsonElement item) => new()
	{
		Name = Text(item, "name"),
		Type = Text(item, "type"),
		SizeBytes = Number(item, "size"),
		FileSystem = Text(item, "fstype"),
		MountPoint = Text(item, "mountpoint"),
		Label = Text(item, "label"),
		Model = Text(item, "model").Trim(),
		ReadOnly = Flag(item, "ro"),
		Removable = Flag(item, "rm"),
		Children = item.TryGetProperty("children", out JsonElement children) && children.ValueKind == JsonValueKind.Array
			? [.. children.EnumerateArray().Where(child => child.ValueKind == JsonValueKind.Object).Select(Device)]
			: [],
	};

	private static string Text(JsonElement item, string name) =>
		item.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

	private static long? Number(JsonElement item, string name)
	{
		if (!item.TryGetProperty(name, out JsonElement value))
		{
			return null;
		}

		return value.ValueKind switch
		{
			JsonValueKind.Number when value.TryGetInt64(out long number) => number,
			JsonValueKind.String when long.TryParse(value.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out long parsed) => parsed,
			_ => null,
		};
	}

	private static bool Flag(JsonElement item, string name) =>
		item.TryGetProperty(name, out JsonElement value) && (value.ValueKind == JsonValueKind.True || (value.ValueKind == JsonValueKind.String && value.GetString() == "1"));
}
