using System.Globalization;

namespace Mokaterm.Modules.Ssh.Admin.Monitoring;

/// <summary>Memory and swap from <c>/proc/meminfo</c>, in KiB, the unit it prints.</summary>
internal sealed record MemoryInfo
{
	/// <summary>The lines every script here reads: what <see cref="Parse"/> needs and nothing else.</summary>
	public const string Command = "grep -E '^(MemTotal|MemAvailable|MemFree|Buffers|Cached|SwapTotal|SwapFree):' /proc/meminfo 2>/dev/null";

	public static readonly MemoryInfo Unknown = new();

	public long? TotalKb { get; init; }

	/// <summary>MemAvailable, or free plus buffers and cache on kernels older than 3.14, which do not report it.</summary>
	public long? AvailableKb { get; init; }

	public long? SwapTotalKb { get; init; }

	public long? SwapFreeKb { get; init; }

	public long? UsedKb => TotalKb is { } total && AvailableKb is { } available ? Math.Max(0, total - available) : null;

	public double? UsedPercent => UsedKb is { } used && TotalKb is { } total && total > 0 ? used * 100.0 / total : null;

	public long? SwapUsedKb => SwapTotalKb is { } total && SwapFreeKb is { } free ? Math.Max(0, total - free) : null;

	public double? SwapUsedPercent => SwapUsedKb is { } used && SwapTotalKb is { } total && total > 0 ? used * 100.0 / total : null;

	/// <summary><c>MemTotal:  8037888 kB</c> lines, in any order; the ones missing stay null.</summary>
	public static MemoryInfo Parse(string? text)
	{
		Dictionary<string, long> values = new(StringComparer.Ordinal);
		foreach (string line in AdminSections.Lines(text))
		{
			string[] words = line.Split((char[])[' ', ':', '\t'], StringSplitOptions.RemoveEmptyEntries);
			if (words.Length >= 2 && long.TryParse(words[1], NumberStyles.None, CultureInfo.InvariantCulture, out long kb))
			{
				values[words[0]] = kb;
			}
		}

		return new MemoryInfo
		{
			TotalKb = Get(values, "MemTotal"),
			AvailableKb = Get(values, "MemAvailable")
				?? (Get(values, "MemFree") is { } free ? free + (Get(values, "Buffers") ?? 0) + (Get(values, "Cached") ?? 0) : null),
			SwapTotalKb = Get(values, "SwapTotal"),
			SwapFreeKb = Get(values, "SwapFree"),
		};
	}

	private static long? Get(Dictionary<string, long> values, string key) => values.TryGetValue(key, out long value) ? value : null;
}
