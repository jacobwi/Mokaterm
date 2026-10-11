using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Components;
using Mokaterm.Modules.Ssh.Docker;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// CPU, memory, network and block IO of one container, sampled with <c>docker stats --no-stream</c> while the panel is
/// visible. A sample takes docker about two seconds (it measures CPU over an interval), and the polling never starts a
/// sample while the last one is still out, so a slow server gets fewer samples rather than a queue of them.
/// </summary>
public sealed partial class DockerStatsPanel : DockerPageBase
{
	private const int HistoryLength = 40;
	private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(3);

	private readonly List<double> _cpuHistory = [];
	private readonly List<double> _memoryHistory = [];
	private DockerContainerStats? _stats;

	[Parameter, EditorRequired]
	public string ContainerId { get; set; } = "";

	private int CoreCount => Math.Max(1, Feature?.Status?.Info?.Cpus ?? 1);

	/// <summary>Full scale for the CPU line: every core busy.</summary>
	private double CpuScale => CoreCount * 100d;

	protected override async Task OnInitializedAsync()
	{
		await RefreshAsync();
		StartPolling(SampleInterval, RefreshAsync);
	}

	private static string BarWidth(double? percent) =>
		string.Create(CultureInfo.InvariantCulture, $"width:{Math.Clamp(percent ?? 0, 0, 100):0.#}%");

	/// <summary>Points for a 100 by 24 box, oldest on the left, the newest sample at the right edge.</summary>
	private static string Spark(List<double> history, double scale)
	{
		if (history.Count < 2)
		{
			return "";
		}

		StringBuilder points = new();
		for (int index = 0; index < history.Count; index++)
		{
			double x = 100d * (HistoryLength - history.Count + index) / (HistoryLength - 1);
			double y = 23d - (22d * Math.Clamp(history[index] / scale, 0, 1));
			points.Append(CultureInfo.InvariantCulture, $"{x:0.##},{y:0.##} ");
		}

		return points.ToString().TrimEnd();
	}

	private static void Remember(List<double> history, double? value)
	{
		history.Add(value ?? 0);
		if (history.Count > HistoryLength)
		{
			history.RemoveAt(0);
		}
	}

	private double? CpuShare(DockerContainerStats stats) => stats.CpuValue is { } cpu ? cpu * 100 / CpuScale : null;

	private Task RefreshAsync() => LoadAsync(async (client, token) =>
	{
		IReadOnlyList<DockerContainerStats> samples = await client.StatsAsync([ContainerId], token);
		_stats = samples.Count > 0 ? samples[0] : null;
		if (_stats is { } stats)
		{
			Remember(_cpuHistory, stats.CpuValue);
			Remember(_memoryHistory, stats.MemoryPercentValue);
		}
	});
}
