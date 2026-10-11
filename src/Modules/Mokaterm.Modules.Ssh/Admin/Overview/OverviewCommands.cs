using System.Globalization;
using Mokaterm.Modules.Ssh.Admin.Storage;

namespace Mokaterm.Modules.Ssh.Admin.Overview;

/// <summary>The overview reads <c>/proc</c> and df; the names and versions come from detection, which already has them.</summary>
internal static class OverviewCommands
{
	public static AdminScript Read() => new()
	{
		Name = "overview.read",
		Body = string.Join('\n',
			AdminSections.Start("uptime"),
			"cat /proc/uptime 2>/dev/null",
			AdminSections.Start("loadavg"),
			"cat /proc/loadavg 2>/dev/null",
			AdminSections.Start("now"),
			"date +%s",
			AdminSections.Start("cpu"),
			"grep -m 1 -E '^(model name|Hardware|cpu model|Processor)[[:space:]]*:' /proc/cpuinfo 2>/dev/null",
			AdminSections.Start("cores"),
			"nproc 2>/dev/null || grep -c '^processor' /proc/cpuinfo 2>/dev/null",
			AdminSections.Start("meminfo"),
			"grep -E '^(MemTotal|MemAvailable|MemFree|Buffers|Cached|SwapTotal|SwapFree):' /proc/meminfo 2>/dev/null",
			AdminSections.Start("df"),
			"df -P -k 2>/dev/null",
			AdminSections.Start("mounts"),
			"cat /proc/mounts 2>/dev/null",
			"exit 0"),
	};

	public static OverviewSnapshot Parse(string output)
	{
		AdminSections sections = AdminSections.Parse(output);
		string[] uptime = First(sections.Get("uptime")).Split(' ', StringSplitOptions.RemoveEmptyEntries);
		string[] load = First(sections.Get("loadavg")).Split(' ', StringSplitOptions.RemoveEmptyEntries);
		Dictionary<string, long> memory = new(StringComparer.Ordinal);
		foreach (string line in AdminSections.Lines(sections.Get("meminfo")))
		{
			string[] words = line.Split((char[])[' ', ':', '\t'], StringSplitOptions.RemoveEmptyEntries);
			if (words.Length >= 2 && long.TryParse(words[1], NumberStyles.None, CultureInfo.InvariantCulture, out long kb))
			{
				memory[words[0]] = kb;
			}
		}

		string cpu = First(sections.Get("cpu"));
		int colon = cpu.IndexOf(':', StringComparison.Ordinal);
		IReadOnlyList<MountEntry> mounts = DiskCommands.ParseMounts(sections.Get("mounts"));
		return new OverviewSnapshot
		{
			Uptime = uptime.Length > 0 && double.TryParse(uptime[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)
				? TimeSpan.FromSeconds(Math.Floor(seconds))
				: null,
			Now = long.TryParse(First(sections.Get("now")), NumberStyles.None, CultureInfo.InvariantCulture, out long now) ? now : null,
			Load1 = Load(load, 0),
			Load5 = Load(load, 1),
			Load15 = Load(load, 2),
			CpuModel = colon < 0 ? "" : cpu[(colon + 1)..].Trim(),
			Cores = int.TryParse(First(sections.Get("cores")), NumberStyles.None, CultureInfo.InvariantCulture, out int cores) && cores > 0 ? cores : null,
			MemoryTotalKb = Get(memory, "MemTotal"),
			MemoryAvailableKb = Get(memory, "MemAvailable")
				?? (Get(memory, "MemFree") is { } free ? free + (Get(memory, "Buffers") ?? 0) + (Get(memory, "Cached") ?? 0) : null),
			SwapTotalKb = Get(memory, "SwapTotal"),
			SwapFreeKb = Get(memory, "SwapFree"),
			Disks = [.. DiskCommands.ParseDf(sections.Get("df"), mounts).Where(DiskCommands.IsStorage)],
		};
	}

	private static string First(string? text) => AdminSections.Lines(text).FirstOrDefault()?.Trim() ?? "";

	private static double? Load(string[] values, int index) =>
		values.Length > index && double.TryParse(values[index], NumberStyles.Float, CultureInfo.InvariantCulture, out double load) ? load : null;

	private static long? Get(Dictionary<string, long> values, string key) => values.TryGetValue(key, out long value) ? value : null;
}
