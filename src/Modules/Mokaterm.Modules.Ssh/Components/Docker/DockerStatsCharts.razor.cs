using System.Globalization;
using Microsoft.AspNetCore.Components;
using Moka.Red.Core.Enums;
using Moka.Red.Data.Chart;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.UI.Common.Components;
using Mokaterm.UI.Common.Formatting;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// One container's figures: its newest CPU, memory, network and disk rates and process count as tiles, then the last
/// two minutes of each as a live chart. The lines are the container's <see cref="SampleHistory"/>s, so whoever samples
/// (its stats tab, or the stats page for every container at once) owns them and this only draws.
/// </summary>
public sealed partial class DockerStatsCharts : ComponentBase
{
	private IReadOnlyList<DockerChart> _charts = [];

	/// <summary>CPU samples as docker prints them: 100 is one core.</summary>
	[Parameter, EditorRequired]
	public IReadOnlyList<double> Cpu { get; set; } = [];

	/// <summary>Memory in use, in bytes.</summary>
	[Parameter, EditorRequired]
	public IReadOnlyList<double> MemoryBytes { get; set; } = [];

	/// <summary>Bytes a second received, then sent, read and written: NaN where there was nothing to measure yet.</summary>
	[Parameter]
	public IReadOnlyList<double> Received { get; set; } = [];

	[Parameter]
	public IReadOnlyList<double> Sent { get; set; } = [];

	[Parameter]
	public IReadOnlyList<double> Read { get; set; } = [];

	[Parameter]
	public IReadOnlyList<double> Written { get; set; } = [];

	/// <summary>The docker host's cores, which is what CPU counts against: four cores can reach 400%.</summary>
	[Parameter]
	public int Cores { get; set; } = 1;

	/// <summary>The newest memory share as docker printed it, such as <c>2.35%</c>.</summary>
	[Parameter]
	public string? MemoryPercent { get; set; }

	/// <summary>The memory the container may use, as docker printed it, such as <c>7.638GiB</c>.</summary>
	[Parameter]
	public string? MemoryLimit { get; set; }

	[Parameter]
	public string? Pids { get; set; }

	/// <summary>The height of each chart's plot.</summary>
	[Parameter]
	public string ChartHeight { get; set; } = "150px";

	private string CpuText => Latest(Cpu) is { } cpu ? DockerChartFormat.Percent(cpu) : "-";

	private string CpuHelp => string.Create(CultureInfo.CurrentCulture, $"of {Math.Max(1, Cores) * 100}%, {Math.Max(1, Cores)} cores");

	private MokaColor? CpuColor => Latest(Cpu) is { } cpu ? LevelColor(cpu / Math.Max(1, Cores)) : null;

	private string MemoryText => Latest(MemoryBytes) is { } bytes ? DockerChartFormat.Bytes(bytes) : "-";

	private string? MemoryHelp => (MemoryPercent, MemoryLimit) switch
	{
		({ Length: > 0 } percent, { Length: > 0 } limit) => $"{percent} of {limit}",
		({ Length: > 0 } percent, _) => percent,
		_ => null,
	};

	private MokaColor? MemoryColor =>
		double.TryParse(MemoryPercent?.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out double share) ? LevelColor(share) : null;

	internal static double? Latest(IReadOnlyList<double> line) => line.Count > 0 && double.IsFinite(line[^1]) ? line[^1] : null;

	// A figure in good order keeps the tile's own colour; only warning and full stand out.
	private static MokaColor? LevelColor(double percent) => UsageBar.LevelOf(percent) switch
	{
		UsageLevel.Full => MokaColor.Error,
		UsageLevel.Warn => MokaColor.Warning,
		_ => null,
	};

	protected override void OnParametersSet() =>
		_charts =
		[
			new DockerChart(
				"CPU",
				"CPU over the last two minutes",
				[new MokaLineSeries { Name = "CPU", Values = SampleHistory.Window(Cpu, DockerStatsHistory.Length), Color = MokaColor.Primary }],
				DockerChartFormat.Percent,
				SuggestedMax: 10),
			new DockerChart(
				"Memory",
				"Memory in use over the last two minutes",
				[new MokaLineSeries { Name = "In use", Values = SampleHistory.Window(MemoryBytes, DockerStatsHistory.Length), Color = MokaColor.Info }],
				DockerChartFormat.Bytes,
				DisplayFormat.ByteUnits),
			new DockerChart(
				"Network",
				"Network traffic over the last two minutes",
				[
					new MokaLineSeries { Name = "In", Values = SampleHistory.Window(Received, DockerStatsHistory.Length), Color = MokaColor.Success },
					new MokaLineSeries { Name = "Out", Values = SampleHistory.Window(Sent, DockerStatsHistory.Length), Color = MokaColor.Info },
				],
				DockerChartFormat.Rate,
				DisplayFormat.ByteUnits,
				SuggestedMax: 1024),
			new DockerChart(
				"Block I/O",
				"Disk reads and writes over the last two minutes",
				[
					new MokaLineSeries { Name = "Read", Values = SampleHistory.Window(Read, DockerStatsHistory.Length), Color = MokaColor.Info },
					new MokaLineSeries { Name = "Write", Values = SampleHistory.Window(Written, DockerStatsHistory.Length), Color = MokaColor.Warning },
				],
				DockerChartFormat.Rate,
				DisplayFormat.ByteUnits,
				SuggestedMax: 1024),
		];
}
