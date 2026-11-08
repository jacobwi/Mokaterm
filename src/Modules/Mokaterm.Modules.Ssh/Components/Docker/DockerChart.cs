using Moka.Red.Data.Chart;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// One live chart of the Docker manager: what <see cref="DockerLiveChart"/> draws. Public because it is a component
/// parameter.
/// </summary>
/// <param name="Title">The card's micro label, such as "CPU".</param>
/// <param name="Label">What the chart shows, for a screen reader.</param>
/// <param name="Series">The lines, each a window of <see cref="Mokaterm.UI.Common.Components.SampleHistory.Window"/>.</param>
/// <param name="Format">Writes a value for the ticks, the legend and the tooltips.</param>
/// <param name="Units">The units <paramref name="Format"/> writes in, so the ticks fall on round values; null for plain numbers.</param>
/// <param name="SuggestedMax">The least the top of the scale reaches, so a quiet line does not fill the chart.</param>
public sealed record DockerChart(
	string Title,
	string Label,
	IReadOnlyList<MokaLineSeries> Series,
	Func<double, string> Format,
	IReadOnlyList<double>? Units = null,
	double? SuggestedMax = null)
{
	/// <summary>Whether the legend ends with the highest value any line reached; a chart of many lines leaves it out.</summary>
	public bool ShowPeak { get; init; } = true;
}
