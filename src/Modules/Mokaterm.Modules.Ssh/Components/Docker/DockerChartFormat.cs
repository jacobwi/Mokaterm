using System.Globalization;
using Mokaterm.UI.Common.Formatting;

namespace Mokaterm.Modules.Ssh.Components.Docker;

internal static class DockerChartFormat
{
	public static string Percent(double value) =>
		string.Create(CultureInfo.CurrentCulture, $"{Math.Max(0, value):0.#}%");

	public static string Bytes(double value) => DisplayFormat.Bytes((long)Math.Max(0, value));

	// DisplayFormat.Rate leaves zero blank for a status bar; a chart's bottom tick and an idle legend still need a number.
	public static string Rate(double value) => value > 0 ? DisplayFormat.Rate(value) : "0 B/s";

	public static string Rate(double? value) => value is { } rate ? Rate(rate) : "-";
}
