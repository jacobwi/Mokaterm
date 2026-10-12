using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Components;

namespace Mokaterm.UI.Common.Components;

/// <summary>
/// A small line of recent samples, oldest on the left: a container's CPU, a host's load in a status bar or a fleet
/// row. It fills the width it is given and is <see cref="Height"/> pixels tall.
/// </summary>
public sealed partial class Sparkline : ComponentBase
{
	internal const int ViewWidth = 100;
	internal const int ViewHeight = 24;

	private string _points = "";

	/// <summary>The samples, oldest first. Negative values draw as zero; fewer than two draw nothing.</summary>
	[Parameter, EditorRequired]
	public IReadOnlyList<double> Values { get; set; } = [];

	/// <summary>
	/// The value at the top of the box, such as 100 for a percentage. Null scales to the largest sample, which shows the
	/// shape of the line but not how high it is.
	/// </summary>
	[Parameter]
	public double? Max { get; set; }

	/// <summary>
	/// How many samples the width holds. The newest always sits at the right edge, so a line that has only started
	/// grows in from the right instead of stretching. Null spreads the samples there are over the whole width.
	/// </summary>
	[Parameter]
	public int? Slots { get; set; }

	/// <summary>What the line shows, for a screen reader, such as "CPU over the last two minutes". Null hides it from one.</summary>
	[Parameter]
	public string? Label { get; set; }

	[Parameter]
	public int Height { get; set; } = 24;

	/// <summary>
	/// A class of the caller's own on the SVG, for its place in a layout. The line takes its colour from
	/// <c>--mt-sparkline-color</c>, which a caller sets on any element around it.
	/// </summary>
	[Parameter]
	public string? Class { get; set; }

	private string CssClass => Class is { Length: > 0 } extra ? $"mt-sparkline {extra}" : "mt-sparkline";

	private string HeightStyle => string.Create(CultureInfo.InvariantCulture, $"height:{Math.Max(1, Height)}px");

	/// <summary>The polyline's points in a <see cref="ViewWidth"/> by <see cref="ViewHeight"/> box, with a pixel of room at the top and the bottom.</summary>
	internal static string Points(IReadOnlyList<double> values, double? max, int? slots)
	{
		ArgumentNullException.ThrowIfNull(values);
		if (values.Count < 2)
		{
			return "";
		}

		int count = values.Count;
		int width = Math.Max(slots ?? count, count);
		double top = max is { } fixedTop && fixedTop > 0 ? fixedTop : values.Max();
		StringBuilder points = new();
		for (int index = 0; index < count; index++)
		{
			double share = top > 0 && double.IsFinite(values[index]) ? Math.Clamp(values[index] / top, 0, 1) : 0;
			double x = (double)ViewWidth * (width - count + index) / (width - 1);
			double y = ViewHeight - 1 - ((ViewHeight - 2) * share);
			if (index > 0)
			{
				points.Append(' ');
			}

			points.Append(CultureInfo.InvariantCulture, $"{x:0.##},{y:0.##}");
		}

		return points.ToString();
	}

	protected override void OnParametersSet() => _points = Points(Values, Max, Slots);
}
