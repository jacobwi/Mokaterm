using System.Globalization;
using Moka.Red.Core.Enums;
using Mokaterm.UI.Common.Formatting;

namespace Mokaterm.Modules.Ssh.Components.Admin;

/// <summary>Numbers as the system view's pages show them. Sizes and timestamps defer to <see cref="DisplayFormat"/>.</summary>
internal static class AdminFormat
{
	/// <summary>A size given in KiB, the unit /proc and df use.</summary>
	public static string Kb(long? kb) => kb is { } value ? DisplayFormat.Bytes(value * 1024) : "";

	/// <inheritdoc cref="DisplayFormat.Span"/>
	public static string Span(TimeSpan? span) => DisplayFormat.Span(span);

	public static string Number(double? value, string format = "0.00") =>
		value is { } number ? number.ToString(format, CultureInfo.InvariantCulture) : "";

	public static string Count(int value) => value.ToString("N0", CultureInfo.CurrentCulture);

	public static string Percent(double percent) => string.Create(CultureInfo.InvariantCulture, $"{percent:0}%");

	/// <summary>Bytes per second, <c>0 B/s</c> when nothing moved, empty when the rate is not known yet.</summary>
	public static string Rate(double? bytesPerSecond) => bytesPerSecond switch
	{
		null => "",
		> 0 => DisplayFormat.Rate(bytesPerSecond.Value),
		_ => "0 B/s",
	};

	/// <summary>A dot colour for a true, false or unknown state.</summary>
	public static MokaColor Dot(bool? state) => state switch
	{
		true => MokaColor.Success,
		false => MokaColor.Error,
		_ => MokaColor.Surface,
	};
}
