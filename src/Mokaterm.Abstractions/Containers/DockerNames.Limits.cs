using System.Globalization;
using System.Text.RegularExpressions;

namespace Mokaterm.Abstractions.Containers;

/// <summary>
/// What <c>docker update</c> takes for a container's limits: a CPU count and memory sizes. Each method returns why the
/// value cannot be used, or null when it can, like the name rules.
/// </summary>
public static partial class DockerNames
{
	/// <summary>The smallest memory limit the daemon accepts: 6 MiB.</summary>
	public const long MinMemoryBytes = 6L * 1024 * 1024;

	/// <summary>The most CPUs a limit may name when the docker host's own count is not known.</summary>
	public const int MaxCpus = 1024;

	/// <summary>
	/// A CPU limit as <c>--cpus</c> takes it: a number of CPUs with at most two decimals, from 0.01 up to what the host
	/// has, such as <c>1.5</c>.
	/// </summary>
	/// <param name="hostCpus">How many CPUs the docker host has, which docker will not let a limit pass; 0 when not known.</param>
	public static string? CpusProblem(string? cpus, int hostCpus = 0)
	{
		string text = cpus?.Trim() ?? "";
		if (text.Length == 0)
		{
			return "Name a number of CPUs, such as 1.5.";
		}

		if (!TryParseCpus(text, out decimal value))
		{
			return "CPUs are a number with at most two decimals, such as 0.5 or 2.";
		}

		int most = hostCpus > 0 ? hostCpus : MaxCpus;
		if (value < 0.01m || value > most)
		{
			return hostCpus > 0
				? string.Create(CultureInfo.CurrentCulture, $"This host has {hostCpus} CPUs, so the limit runs from 0.01 to {hostCpus}.")
				: string.Create(CultureInfo.CurrentCulture, $"The limit runs from 0.01 to {MaxCpus} CPUs.");
		}

		return null;
	}

	/// <summary>Reads a CPU count written the way <see cref="CpusProblem"/> accepts it.</summary>
	public static bool TryParseCpus(string? text, out decimal cpus)
	{
		cpus = 0;
		string trimmed = text?.Trim() ?? "";
		return trimmed.Length is > 0 and <= 16
			&& CpuCount().IsMatch(trimmed)
			&& decimal.TryParse(trimmed, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out cpus);
	}

	/// <summary>
	/// A memory limit as <c>--memory</c> takes it: a number and an optional unit, counted in 1024s the way docker counts
	/// them (<c>512m</c>, <c>1.5g</c>, <c>2GiB</c>), and at least <see cref="MinMemoryBytes"/>.
	/// </summary>
	public static string? MemoryProblem(string? memory)
	{
		string text = memory?.Trim() ?? "";
		if (text.Length == 0)
		{
			return "Name a size, such as 512m or 2g.";
		}

		if (!TryParseMemory(text, out long bytes))
		{
			return "A size is a number with an optional unit: b, k, m, g, t or p, such as 512m or 1.5g.";
		}

		return bytes < MinMemoryBytes ? "Docker takes no memory limit below 6m." : null;
	}

	/// <summary>
	/// A swap limit as <c>--memory-swap</c> takes it: memory and swap together as a size (<see cref="MemoryProblem"/>),
	/// or <c>-1</c> for as much swap as the host has.
	/// </summary>
	public static string? MemorySwapProblem(string? swap)
	{
		string text = swap?.Trim() ?? "";
		if (text == "-1")
		{
			return null;
		}

		if (text.Length == 0)
		{
			return "Name a size, such as 1g, or -1 for no swap limit.";
		}

		return TryParseMemory(text, out _)
			? null
			: "A swap limit is a size, such as 1g, or -1 for no swap limit.";
	}

	/// <summary>
	/// Reads a size the way docker's own parser does: a number, one optional space, an optional unit letter of
	/// <c>bkmgtp</c> and an optional <c>i</c> and <c>b</c>, in any case, each unit 1024 times the one before.
	/// </summary>
	public static bool TryParseMemory(string? text, out long bytes)
	{
		bytes = 0;
		string trimmed = text?.Trim() ?? "";
		Match match = trimmed.Length is > 0 and <= 32 ? MemorySize().Match(trimmed) : Match.Empty;
		if (!match.Success || !decimal.TryParse(match.Groups["number"].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal number))
		{
			return false;
		}

		int power = match.Groups["unit"].Value.ToUpperInvariant() switch
		{
			"K" => 1,
			"M" => 2,
			"G" => 3,
			"T" => 4,
			"P" => 5,
			_ => 0,
		};

		decimal total = number;
		for (int step = 0; step < power; step++)
		{
			total *= 1024;
		}

		if (total >= long.MaxValue)
		{
			return false;
		}

		bytes = (long)decimal.Truncate(total);
		return true;
	}

	[GeneratedRegex(@"^[0-9]{1,4}(\.[0-9]{1,2})?\z", RegexOptions.CultureInvariant)]
	private static partial Regex CpuCount();

	[GeneratedRegex(@"^(?<number>[0-9]+(\.[0-9]+)?) ?(?<unit>[kKmMgGtTpP])?[iI]?[bB]?\z", RegexOptions.CultureInvariant)]
	private static partial Regex MemorySize();
}
