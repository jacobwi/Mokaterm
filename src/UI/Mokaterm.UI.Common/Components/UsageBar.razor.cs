using System.Globalization;
using Microsoft.AspNetCore.Components;

namespace Mokaterm.UI.Common.Components;

/// <summary>
/// How much of something is used, as a bar that turns to the warning colour at <see cref="WarnPercent"/> and to the
/// error colour at <see cref="FullPercent"/>.
/// </summary>
public sealed partial class UsageBar : ComponentBase
{
	public const double WarnPercent = 75;

	public const double FullPercent = 90;

	/// <summary>From 0 to 100; anything outside is drawn at the nearer end, and null as an empty bar.</summary>
	[Parameter]
	public double? Percent { get; set; }

	/// <summary>What is measured, such as "Memory used", which makes the bar a meter. Null leaves it to the number beside it.</summary>
	[Parameter]
	public string? Label { get; set; }

	/// <summary>A class of the caller's own, for its place in a layout.</summary>
	[Parameter]
	public string? Class { get; set; }

	private double Clamped => Percent is { } percent && double.IsFinite(percent) ? Math.Clamp(percent, 0, 100) : 0;

	private string ValueText => Clamped.ToString("0.#", CultureInfo.InvariantCulture);

	private string WidthStyle => $"width:{ValueText}%";

	private string CssClass
	{
		get
		{
			string level = LevelOf(Clamped) switch
			{
				UsageLevel.Full => "mt-usage mt-usage--full",
				UsageLevel.Warn => "mt-usage mt-usage--warn",
				_ => "mt-usage",
			};

			return Class is { Length: > 0 } extra ? $"{level} {extra}" : level;
		}
	}

	/// <summary>Where <paramref name="percent"/> falls, for anything else that colours a number the way the bar does.</summary>
	public static UsageLevel LevelOf(double percent) => percent switch
	{
		>= FullPercent => UsageLevel.Full,
		>= WarnPercent => UsageLevel.Warn,
		_ => UsageLevel.Normal,
	};
}
