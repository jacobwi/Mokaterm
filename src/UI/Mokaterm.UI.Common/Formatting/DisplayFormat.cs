using System.Globalization;

namespace Mokaterm.UI.Common.Formatting;

/// <summary>Human-readable sizes, rates and times, shared by every view that shows them.</summary>
public static class DisplayFormat
{
	private static readonly string[] SizeUnits = ["B", "KB", "MB", "GB", "TB", "PB"];

	/// <summary><c>0 B</c>, <c>512 B</c>, <c>12.4 MB</c>. Binary multiples, one decimal above bytes.</summary>
	public static string Bytes(long bytes)
	{
		if (bytes < 1024)
		{
			return string.Create(CultureInfo.CurrentCulture, $"{Math.Max(bytes, 0)} B");
		}

		double value = bytes;
		int unit = 0;
		while (value >= 1024 && unit < SizeUnits.Length - 1)
		{
			value /= 1024;
			unit++;
		}

		return string.Create(CultureInfo.CurrentCulture, $"{value:0.#} {SizeUnits[unit]}");
	}

	public static string Rate(double bytesPerSecond) =>
		bytesPerSecond <= 0 ? "" : Bytes((long)bytesPerSecond) + "/s";

	/// <summary><c>just now</c>, <c>5 min ago</c>, <c>3 h ago</c>, then a short date.</summary>
	public static string Relative(DateTimeOffset value, DateTimeOffset now)
	{
		TimeSpan age = now - value;
		return age.TotalSeconds switch
		{
			< 45 => "just now",
			< 3600 => string.Create(CultureInfo.CurrentCulture, $"{Math.Max(1, (int)age.TotalMinutes)} min ago"),
			< 86400 => string.Create(CultureInfo.CurrentCulture, $"{(int)age.TotalHours} h ago"),
			< 7 * 86400 => string.Create(CultureInfo.CurrentCulture, $"{(int)age.TotalDays} d ago"),
			_ => value.ToLocalTime().ToString("d", CultureInfo.CurrentCulture),
		};
	}

	/// <summary>Sortable local timestamp for file listings: <c>2026-09-17 14:05</c>.</summary>
	public static string Timestamp(DateTimeOffset? value) =>
		value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "";

	/// <summary><c>12d 4h</c>, <c>4h 13m</c>, <c>13m</c>: the two largest units, enough for an uptime. Empty for null.</summary>
	public static string Span(TimeSpan? span)
	{
		if (span is not { } value)
		{
			return "";
		}

		if (value.TotalDays >= 1)
		{
			return string.Create(CultureInfo.InvariantCulture, $"{(int)value.TotalDays}d {value.Hours}h");
		}

		return value.TotalHours >= 1
			? string.Create(CultureInfo.InvariantCulture, $"{(int)value.TotalHours}h {value.Minutes}m")
			: string.Create(CultureInfo.InvariantCulture, $"{Math.Max(0, value.Minutes)}m");
	}

	/// <summary><c>1:05:09</c> or <c>05:09</c>.</summary>
	public static string Duration(TimeSpan value) =>
		value.TotalHours >= 1
			? string.Create(CultureInfo.InvariantCulture, $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}")
			: string.Create(CultureInfo.InvariantCulture, $"{value.Minutes:00}:{value.Seconds:00}");
}
