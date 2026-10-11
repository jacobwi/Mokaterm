using System.Globalization;
using Moka.Red.Core.Enums;
using Mokaterm.UI.Common.Formatting;

namespace Mokaterm.Modules.Ssh.Components.Admin;

/// <summary>Numbers as the system view's pages show them. Sizes and timestamps defer to <see cref="DisplayFormat"/>.</summary>
internal static class AdminFormat
{
	/// <summary>A size given in KiB, the unit /proc and df use.</summary>
	public static string Kb(long? kb) => kb is { } value ? DisplayFormat.Bytes(value * 1024) : "";

	/// <summary><c>12d 4h</c>, <c>4h 13m</c>, <c>13m</c>: the two largest units, enough for an uptime.</summary>
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

	public static string Number(double? value, string format = "0.00") =>
		value is { } number ? number.ToString(format, CultureInfo.InvariantCulture) : "";

	public static string Count(int value) => value.ToString("N0", CultureInfo.CurrentCulture);

	/// <summary>The usage bar's modifier: warning past 75 percent, full past 90.</summary>
	public static string BarClass(double percent) => percent switch
	{
		>= 90 => "mta-bar mta-bar--full",
		>= 75 => "mta-bar mta-bar--warn",
		_ => "mta-bar",
	};

	public static string BarWidth(double percent) =>
		string.Create(CultureInfo.InvariantCulture, $"width:{Math.Clamp(percent, 0, 100):0.#}%");

	public static string Percent(double percent) => string.Create(CultureInfo.InvariantCulture, $"{percent:0}%");

	/// <summary>A dot colour for a true, false or unknown state.</summary>
	public static MokaColor Dot(bool? state) => state switch
	{
		true => MokaColor.Success,
		false => MokaColor.Error,
		_ => MokaColor.Surface,
	};
}
