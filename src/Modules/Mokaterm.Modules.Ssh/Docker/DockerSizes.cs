using System.Globalization;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// Reads the sizes docker prints. Images and disk usage use decimal units (<c>187MB</c>, <c>1.2kB</c>), memory uses binary
/// ones (<c>10.2MiB</c>), and a size docker does not know is <c>N/A</c> or empty.
/// </summary>
internal static class DockerSizes
{
	private static readonly (string Unit, double Factor)[] Units =
	[
		("KiB", 1024d),
		("MiB", 1024d * 1024),
		("GiB", 1024d * 1024 * 1024),
		("TiB", 1024d * 1024 * 1024 * 1024),
		("PiB", 1024d * 1024 * 1024 * 1024 * 1024),
		("kB", 1e3),
		("KB", 1e3),
		("MB", 1e6),
		("GB", 1e9),
		("TB", 1e12),
		("PB", 1e15),
		("B", 1d),
	];

	/// <summary>Bytes, or null for <c>N/A</c>, an empty field or anything else that is not a size.</summary>
	public static long? Parse(string? text)
	{
		string value = text?.Trim() ?? "";

		// "1.2GB (50%)" in disk usage, "32.5MB (virtual 187MB)" in old ps output: the first size is the one.
		int space = value.IndexOf(' ', StringComparison.Ordinal);
		if (space > 0)
		{
			value = value[..space];
		}

		foreach ((string unit, double factor) in Units)
		{
			if (value.EndsWith(unit, StringComparison.Ordinal)
				&& double.TryParse(value[..^unit.Length], NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
				&& number >= 0)
			{
				return (long)Math.Round(number * factor);
			}
		}

		return null;
	}

	/// <summary>The two sizes of <c>10.2MiB / 7.6GiB</c> or <c>1.2kB / 0B</c>.</summary>
	public static (long? First, long? Second) ParsePair(string? text)
	{
		string[] parts = (text ?? "").Split('/', 2, StringSplitOptions.TrimEntries);
		return parts.Length == 2 ? (Parse(parts[0]), Parse(parts[1])) : (Parse(parts[0]), null);
	}

	/// <summary><c>12.34%</c> as 12.34, or null.</summary>
	public static double? ParsePercent(string? text)
	{
		string value = text?.Trim().TrimEnd('%').Trim() ?? "";
		return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && double.IsFinite(number)
			? number
			: null;
	}
}
