using System.Globalization;
using Moka.Red.Core.Enums;
using Moka.Red.Data.Chart;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.UI.Common.Components;
using Mokaterm.UI.Common.Formatting;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// Every running container's CPU, memory, network and disk rates and process count, from one
/// <c>docker stats --no-stream</c> every few seconds while the page shows: the totals as tiles, the last two minutes as
/// live charts (CPU and memory per container, the busiest five, network and disk all together), then one row per
/// container with its recent CPU as a line. Any column sorts; the busiest CPU comes first until the user picks another.
/// A row opens that container's own charts beside the list, drawn from the same samples.
/// </summary>
public sealed partial class DockerStatsPage : DockerManagerPageBase
{
	internal const int ChartedContainers = 5;

	private readonly DockerStatsHistory _history = new();
	private IReadOnlyList<DockerContainerStats> _samples = [];
	private IReadOnlyList<DockerContainerStats> _rows = [];
	private IReadOnlyList<DockerChart> _charts = [];
	private double[] _received = [];
	private double[] _sent = [];
	private double[] _read = [];
	private double[] _written = [];
	private string _query = "";
	private string? _selected;

	private int CoreCount => Math.Max(1, Feature?.Status?.Info?.Cpus ?? 1);

	private double CpuScale => CoreCount * 100d;

	private DockerContainerStats? SelectedRow => _selected is { } id ? _samples.FirstOrDefault(row => row.Id == id) : null;

	private double TotalCpu => _rows.Sum(row => row.CpuValue ?? 0);

	private double TotalMemory => _rows.Sum(row => row.MemoryBytes ?? 0);

	private string? RunningHelp => _query.Length == 0
		? null
		: string.Create(CultureInfo.CurrentCulture, $"of {_samples.Count} running");

	private string? MemoryHelp => Feature?.Status?.Info is { MemoryBytes: > 0 } info
		? string.Create(CultureInfo.CurrentCulture, $"{TotalMemory * 100 / info.MemoryBytes:0.#}% of {DisplayFormat.Bytes(info.MemoryBytes)}")
		: null;

	private string? Summary => IsFirstLoad
		? null
		: string.Create(
			CultureInfo.CurrentCulture,
			$"{_samples.Count} running · a sample every {DockerStatsHistory.SampleInterval.TotalSeconds:0} s, two minutes drawn · rates over 30 s");

	// One top for every row's CPU line, the highest sample any shown container had and at least 10%, so the lines can be
	// compared row to row and an idle container's noise stays flat. Docker's own full scale, every core, flattens them all.
	private double SparkTop => Math.Max(10, _rows.SelectMany(row => _history.Cpu(row.Id)).Where(double.IsFinite).DefaultIfEmpty(0).Max());

	protected override async Task OnInitializedAsync()
	{
		await RefreshAsync();
		StartPolling(DockerStatsHistory.SampleInterval, RefreshAsync);
	}

	// Handed over in name order rather than by size, so a line keeps its colour while the order of the busiest changes
	// from sample to sample.
	internal static IReadOnlyList<DockerContainerStats> Busiest(IReadOnlyList<DockerContainerStats> rows, Func<string, IReadOnlyList<double>> line) =>
		[.. rows
			.OrderByDescending(row => Average(line(row.Id)))
			.ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
			.Take(ChartedContainers)
			.OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase)];

	private static double Average(IReadOnlyList<double> line)
	{
		double[] finite = [.. line.Where(double.IsFinite)];
		return finite.Length == 0 ? 0 : finite.Average();
	}

	private static double? Latest(double[] window) => DockerStatsCharts.Latest(window);

	private static string MemoryUsed(DockerContainerStats row) => row.MemoryBytes is { } bytes ? DisplayFormat.Bytes(bytes) : "-";

	private static string RatePair(double? first, double? second) => $"{DockerChartFormat.Rate(first)} / {DockerChartFormat.Rate(second)}";

	private static int ByName(DockerContainerStats left, DockerContainerStats right) => StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);

	// A figure docker did not give sorts as the smallest, so it ends up last when the largest come first.
	private static int ByCpu(DockerContainerStats left, DockerContainerStats right) => (left.CpuValue ?? -1).CompareTo(right.CpuValue ?? -1);

	private static int ByMemory(DockerContainerStats left, DockerContainerStats right) => (left.MemoryBytes ?? -1).CompareTo(right.MemoryBytes ?? -1);

	private static int ByPids(DockerContainerStats left, DockerContainerStats right) => (left.PidCount ?? -1).CompareTo(right.PidCount ?? -1);

	private int ByNetwork(DockerContainerStats left, DockerContainerStats right) =>
		Both(_history.RatesOf(left.Id).Received, _history.RatesOf(left.Id).Sent).CompareTo(Both(_history.RatesOf(right.Id).Received, _history.RatesOf(right.Id).Sent));

	private int ByBlock(DockerContainerStats left, DockerContainerStats right) =>
		Both(_history.RatesOf(left.Id).Read, _history.RatesOf(left.Id).Written).CompareTo(Both(_history.RatesOf(right.Id).Read, _history.RatesOf(right.Id).Written));

	private static double Both(double? first, double? second) => first is null && second is null ? -1 : (first ?? 0) + (second ?? 0);

	private Task RefreshAsync() => LoadAsync(async (client, token) =>
	{
		_samples = await client.StatsAsync([], token);
		_history.Record(_samples, Time.GetUtcNow());
		Arrange();
	});

	private void OnQueryChanged(string value)
	{
		_query = value?.Trim() ?? "";
		Arrange();
	}

	private void Select(DockerContainerStats row) => _selected = _selected == row.Id ? null : row.Id;

	private void ClosePanel() => _selected = null;

	// Handed to the table in name order: its sort keeps that order among equal figures, so rows do not swap places on
	// every sample.
	private void Arrange()
	{
		_rows = [.. _samples
			.Where(row => _query.Length == 0 || row.Name.Contains(_query, StringComparison.OrdinalIgnoreCase))
			.OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase)];

		_received = DockerStatsHistory.Sum(_rows.Select(row => _history.Received(row.Id)));
		_sent = DockerStatsHistory.Sum(_rows.Select(row => _history.Sent(row.Id)));
		_read = DockerStatsHistory.Sum(_rows.Select(row => _history.Read(row.Id)));
		_written = DockerStatsHistory.Sum(_rows.Select(row => _history.Written(row.Id)));

		_charts =
		[
			new DockerChart(
				"CPU by container",
				"CPU of the busiest containers over the last two minutes",
				PerContainer(Busiest(_rows, _history.Cpu), _history.Cpu),
				DockerChartFormat.Percent,
				SuggestedMax: 10) { ShowPeak = false },
			new DockerChart(
				"Memory by container",
				"Memory of the largest containers over the last two minutes",
				PerContainer(Busiest(_rows, _history.MemoryBytes), _history.MemoryBytes),
				DockerChartFormat.Bytes,
				DisplayFormat.ByteUnits) { ShowPeak = false },
			new DockerChart(
				"Network, all shown",
				"Network traffic of the shown containers together over the last two minutes",
				[
					new MokaLineSeries { Name = "In", Values = _received, Color = MokaColor.Success },
					new MokaLineSeries { Name = "Out", Values = _sent, Color = MokaColor.Info },
				],
				DockerChartFormat.Rate,
				DisplayFormat.ByteUnits,
				SuggestedMax: 1024),
			new DockerChart(
				"Disk, all shown",
				"Disk reads and writes of the shown containers together over the last two minutes",
				[
					new MokaLineSeries { Name = "Read", Values = _read, Color = MokaColor.Info },
					new MokaLineSeries { Name = "Write", Values = _written, Color = MokaColor.Warning },
				],
				DockerChartFormat.Rate,
				DisplayFormat.ByteUnits,
				SuggestedMax: 1024),
		];
	}

	private static MokaLineSeries[] PerContainer(IReadOnlyList<DockerContainerStats> rows, Func<string, IReadOnlyList<double>> line) =>
		[.. rows.Select(row => new MokaLineSeries { Name = row.Name, Values = SampleHistory.Window(line(row.Id), DockerStatsHistory.Length) })];
}
