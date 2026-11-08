using System.Globalization;
using Microsoft.AspNetCore.Components;

namespace Mokaterm.UI.Common.Components;

/// <summary>
/// A small line of recent samples, oldest on the left: a container's CPU, a host's load in a status bar or a fleet
/// row. It fills the width it is given and is <see cref="Height"/> pixels tall, drawn with Moka.Red's sparkline.
/// </summary>
public sealed partial class Sparkline : ComponentBase
{
	private double[] _window = [];
	private double _top = 1;

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
	/// A class of the caller's own on the line's box, for its place in a layout. The line takes its colour from
	/// <c>--mt-sparkline-color</c>, which a caller sets on any element around it or through this class.
	/// </summary>
	[Parameter]
	public string? Class { get; set; }

	private string CssClass => Class is { Length: > 0 } extra ? $"mt-sparkline {extra}" : "mt-sparkline";

	private string HeightStyle => string.Create(CultureInfo.InvariantCulture, $"height:{Math.Max(1, Height)}px");

	internal static double[] Window(IReadOnlyList<double> values, int? slots)
	{
		ArgumentNullException.ThrowIfNull(values);
		double[] cleaned = [.. values.Select(value => double.IsFinite(value) ? Math.Max(0, value) : 0)];
		return SampleHistory.Window(cleaned, slots ?? cleaned.Length);
	}

	// 1 when nothing is above zero: Moka's sparkline draws a flat line through the middle, and zeros belong on the bottom.
	internal static double Top(IReadOnlyList<double> values, double? max)
	{
		ArgumentNullException.ThrowIfNull(values);
		if (max is { } fixedTop && fixedTop > 0 && double.IsFinite(fixedTop))
		{
			return fixedTop;
		}

		double largest = values.Where(double.IsFinite).DefaultIfEmpty(0).Max();
		return largest > 0 ? largest : 1;
	}

	protected override void OnParametersSet()
	{
		_window = Window(Values, Slots);
		_top = Top(Values, Max);
	}
}
