using System.Globalization;
using Mokaterm.Modules.Ssh.Admin.Monitoring;
using Mokaterm.Modules.Ssh.Admin.Storage;

namespace Mokaterm.Modules.Ssh.Admin.Overview;

/// <summary>
/// The overview reads <c>/proc</c> and df, and starts with a live sample so its figures are there before the first
/// tick; the names and versions come from detection, which already has them.
/// </summary>
internal static class OverviewCommands
{
	public static AdminScript Read() => new()
	{
		Name = "overview.read",
		Body = string.Join('\n', [
			.. MonitorCommands.SampleLines,
			AdminSections.Start("now"),
			"date +%s",
			AdminSections.Start("cpu"),
			"grep -m 1 -E '^(model name|Hardware|cpu model|Processor)[[:space:]]*:' /proc/cpuinfo 2>/dev/null",
			AdminSections.Start("cores"),
			"nproc 2>/dev/null || grep -c '^processor' /proc/cpuinfo 2>/dev/null",
			AdminSections.Start("df"),
			"df -P -k 2>/dev/null",
			AdminSections.Start("mounts"),
			"cat /proc/mounts 2>/dev/null",
			"exit 0"]),
	};

	public static OverviewSnapshot Parse(string output)
	{
		AdminSections sections = AdminSections.Parse(output);
		SystemSample sample = MonitorCommands.Parse(sections);
		string cpu = First(sections.Get("cpu"));
		int colon = cpu.IndexOf(':', StringComparison.Ordinal);
		IReadOnlyList<MountEntry> mounts = DiskCommands.ParseMounts(sections.Get("mounts"));
		return new OverviewSnapshot
		{
			Sample = sample,
			Uptime = sample.UptimeSeconds is { } seconds ? TimeSpan.FromSeconds(Math.Floor(seconds)) : null,
			Now = long.TryParse(First(sections.Get("now")), NumberStyles.None, CultureInfo.InvariantCulture, out long now) ? now : null,
			Load1 = sample.Load?.One,
			Load5 = sample.Load?.Five,
			Load15 = sample.Load?.Fifteen,
			CpuModel = colon < 0 ? "" : cpu[(colon + 1)..].Trim(),
			Cores = int.TryParse(First(sections.Get("cores")), NumberStyles.None, CultureInfo.InvariantCulture, out int cores) && cores > 0 ? cores : null,
			MemoryTotalKb = sample.Memory.TotalKb,
			MemoryAvailableKb = sample.Memory.AvailableKb,
			SwapTotalKb = sample.Memory.SwapTotalKb,
			SwapFreeKb = sample.Memory.SwapFreeKb,
			Disks = [.. DiskCommands.ParseDf(sections.Get("df"), mounts).Where(DiskCommands.IsStorage)],
		};
	}

	private static string First(string? text) => AdminSections.Lines(text).FirstOrDefault()?.Trim() ?? "";
}
