using System.Globalization;
using Moka.Red.Data.Table;
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
	private static readonly IReadOnlyList<(StatsColumn Column, string Text)> Columns =
	[
		(StatsColumn.Name, "Container"),
		(StatsColumn.Cpu, "CPU"),
		(StatsColumn.Memory, "Memory"),
		(StatsColumn.Network, "Net in / out"),
		(StatsColumn.Block, "Block read / write"),
		(StatsColumn.Pids, "PIDs"),
	];

	private readonly DockerStatsHistory _history = new();
	private IReadOnlyList<DockerContainerStats> _samples = [];
	private IReadOnlyList<DockerContainerStats> _rows = [];
	private StatsColumn _sort = StatsColumn.Cpu;
	private bool _descending = true;
	private string _query = "";

	/// <summary>What the table can be sorted by.</summary>
	private enum StatsColumn
	{
		Name,
		Cpu,
		Memory,
		Network,
		Block,
		Pids,
	}

	private int CoreCount => Math.Max(1, Feature?.Status?.Info?.Cpus ?? 1);

	private double CpuScale => CoreCount * 100d;

	private string CountText => string.Create(CultureInfo.CurrentCulture, $"{_samples.Count} running");

	protected override async Task OnInitializedAsync()
	{
		await RefreshAsync();
		StartPolling(DockerStatsHistory.SampleInterval, RefreshAsync);
	}

	private static string MemoryUsed(DockerContainerStats row) => row.MemoryBytes is { } bytes ? DisplayFormat.Bytes(bytes) : "";

	private static string ColumnClass(StatsColumn column) => column is StatsColumn.Network or StatsColumn.Block ? "mt-dks-optional" : "";

	private MokaSortDirection DirectionOf(StatsColumn column) =>
		column != _sort ? MokaSortDirection.None
		: _descending ? MokaSortDirection.Descending
		: MokaSortDirection.Ascending;

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

	/// <summary>A second click on the sorted column turns it around; another column starts with its largest first, names with A.</summary>
	private void SortBy(StatsColumn column)
	{
		_descending = column == _sort ? !_descending : column != StatsColumn.Name;
		_sort = column;
		Arrange();
	}

	private void Arrange()
	{
		IEnumerable<DockerContainerStats> shown = _samples.Where(row => _query.Length == 0 || row.Name.Contains(_query, StringComparison.OrdinalIgnoreCase));
		if (_sort == StatsColumn.Name)
		{
			_rows = _descending
				? [.. shown.OrderByDescending(row => row.Name, StringComparer.OrdinalIgnoreCase)]
				: [.. shown.OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase)];
			return;
		}

		Func<DockerContainerStats, double> key = _sort switch
		{
			StatsColumn.Memory => row => row.MemoryBytes ?? -1,
			StatsColumn.Network => row => row.NetworkBytes ?? -1,
			StatsColumn.Block => row => row.BlockBytes ?? -1,
			StatsColumn.Pids => row => row.PidCount ?? -1,
			_ => row => row.CpuValue ?? -1,
		};

		// Ties keep a steady order by name, so rows do not swap places on every sample.
		_rows = _descending
			? [.. shown.OrderByDescending(key).ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)]
			: [.. shown.OrderBy(key).ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)];
	}
}
