using Microsoft.AspNetCore.Components;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.UI.Common.Components;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// CPU, memory, network and block IO of one container, sampled with <c>docker stats --no-stream</c> while the panel is
/// visible. A sample takes docker about two seconds (it measures CPU over an interval), and the polling never starts a
/// sample while the last one is still out, so a slow server gets fewer samples rather than a queue of them.
/// </summary>
public sealed partial class DockerStatsPanel : DockerPageBase
{
	private readonly DockerStatsHistory _history = new();
	private DockerContainerStats? _stats;

	[Parameter, EditorRequired]
	public string ContainerId { get; set; } = "";

	private int CoreCount => Math.Max(1, Feature?.Status?.Info?.Cpus ?? 1);

	/// <summary>Full scale for the CPU line: every core busy.</summary>
	private double CpuScale => CoreCount * 100d;

	protected override async Task OnInitializedAsync()
	{
		await RefreshAsync();
		StartPolling(DockerStatsHistory.SampleInterval, RefreshAsync);
	}

	private Task RefreshAsync() => LoadAsync(async (client, token) =>
	{
		IReadOnlyList<DockerContainerStats> samples = await client.StatsAsync([ContainerId], token);
		_stats = samples.Count > 0 ? samples[0] : null;
		if (_stats is { } stats)
		{
			_history.Record([stats]);
		}
	});
}
