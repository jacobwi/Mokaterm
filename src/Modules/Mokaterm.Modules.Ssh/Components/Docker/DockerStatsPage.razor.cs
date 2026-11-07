using System.Globalization;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.UI.Common.Formatting;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// Every running container's CPU, memory, network and block IO and process count in one table, from one
/// <c>docker stats --no-stream</c> every few seconds while the page shows, each with its recent CPU as a line. Any
/// column sorts; the busiest CPU comes first until the user picks another.
/// </summary>
public sealed partial class DockerStatsPage : DockerManagerPageBase
{
	private readonly DockerStatsHistory _history = new();
	private IReadOnlyList<DockerContainerStats> _samples = [];
	private IReadOnlyList<DockerContainerStats> _rows = [];
	private string _query = "";

	private int CoreCount => Math.Max(1, Feature?.Status?.Info?.Cpus ?? 1);

	private double CpuScale => CoreCount * 100d;

	// CPU counts every core, which is worth saying next to the figures it explains.
	private string? Summary => IsFirstLoad
		? null
		: string.Create(
			CultureInfo.CurrentCulture,
			$"{_samples.Count} running, sampled every {DockerStatsHistory.SampleInterval.TotalSeconds:0} s · {CoreCount} cores, so CPU can reach {CoreCount * 100}% · the lines show the last two minutes");

	protected override async Task OnInitializedAsync()
	{
		await RefreshAsync();
		StartPolling(DockerStatsHistory.SampleInterval, RefreshAsync);
	}

	private static string MemoryUsed(DockerContainerStats row) => row.MemoryBytes is { } bytes ? DisplayFormat.Bytes(bytes) : "";

	private static int ByName(DockerContainerStats left, DockerContainerStats right) => StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);

	// A figure docker did not give sorts as the smallest, so it ends up last when the largest come first.
	private static int ByCpu(DockerContainerStats left, DockerContainerStats right) => (left.CpuValue ?? -1).CompareTo(right.CpuValue ?? -1);

	private static int ByMemory(DockerContainerStats left, DockerContainerStats right) => (left.MemoryBytes ?? -1).CompareTo(right.MemoryBytes ?? -1);

	private static int ByNetwork(DockerContainerStats left, DockerContainerStats right) => (left.NetworkBytes ?? -1).CompareTo(right.NetworkBytes ?? -1);

	private static int ByBlock(DockerContainerStats left, DockerContainerStats right) => (left.BlockBytes ?? -1).CompareTo(right.BlockBytes ?? -1);

	private static int ByPids(DockerContainerStats left, DockerContainerStats right) => (left.PidCount ?? -1).CompareTo(right.PidCount ?? -1);

	private Task RefreshAsync() => LoadAsync(async (client, token) =>
	{
		_samples = await client.StatsAsync([], token);
		_history.Record(_samples);
		Arrange();
	});

	private void OnQueryChanged(string value)
	{
		_query = value?.Trim() ?? "";
		Arrange();
	}

	// Handed to the table in name order: its sort keeps that order among equal figures, so rows do not swap places on
	// every sample.
	private void Arrange() =>
		_rows = [.. _samples
			.Where(row => _query.Length == 0 || row.Name.Contains(_query, StringComparison.OrdinalIgnoreCase))
			.OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase)];
}
