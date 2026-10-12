using System.Globalization;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.UI.Common.Formatting;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// What Docker keeps on disk and what can go: the summary docker system df prints, then stopped containers, dangling
/// or unused images, unused volumes and unused build cache from its verbose listing, each list exactly what its button
/// removes. The images and volumes pages send their "remove unused" here, so there is one place that prunes.
/// </summary>
public sealed partial class DockerCleanupPage : DockerManagerPageBase
{
	/// <summary>How many rows a list shows before "Show all"; the command always names every one.</summary>
	private const int ShownRows = 50;

	private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);
	private IReadOnlyList<DockerDiskUsage> _summary = [];
	private DockerSystemUsage? _usage;
	private IReadOnlyList<DockerContainer> _containers = [];
	private IReadOnlyList<DockerImage> _images = [];
	private IReadOnlyList<DockerVolume> _volumes = [];
	private IReadOnlyList<DockerBuildCacheEntry> _cache = [];
	private bool _taggedImages;
	private bool _namedVolumes;

	private int DanglingCount => _usage is null ? 0 : DockerCleanupPlans.Images(_usage, tagged: false).Count;

	private int UnusedImageCount => _usage is null ? 0 : DockerCleanupPlans.Images(_usage, tagged: true).Count;

	private int AnonymousCount => _usage is null ? 0 : DockerCleanupPlans.Volumes(_usage, named: false).Count;

	private int UnusedVolumeCount => _usage is null ? 0 : DockerCleanupPlans.Volumes(_usage, named: true).Count;

	protected override Task OnInitializedAsync() => RefreshAsync();

	private static string Totals(int count, long bytes) => count == 0
		? ""
		: string.Create(CultureInfo.CurrentCulture, $"{count} · {DisplayFormat.Bytes(bytes)}");

	private static string RemoveText(int count) => string.Create(CultureInfo.CurrentCulture, $"Remove {count}");

	private Task RefreshAsync() => LoadAsync(async (client, token) =>
	{
		Task<IReadOnlyList<DockerDiskUsage>> summary = client.DiskUsageAsync(token);
		Task<DockerSystemUsage?> usage = client.SystemUsageAsync(token);
		await Task.WhenAll(summary, usage);
		_summary = await summary;
		_usage = await usage;
		Select();
	});

	private void Select()
	{
		if (_usage is not { } usage)
		{
			return;
		}

		_containers = DockerCleanupPlans.StoppedContainers(usage);
		_images = DockerCleanupPlans.Images(usage, _taggedImages);
		_volumes = DockerCleanupPlans.Volumes(usage, _namedVolumes);
		_cache = DockerCleanupPlans.BuildCache(usage);
	}

	private void OnImageScopeChanged(string? value)
	{
		_taggedImages = value == "unused";
		Select();
	}

	private void OnVolumeScopeChanged(string? value)
	{
		_namedVolumes = value == "all";
		Select();
	}

	private IEnumerable<T> Capped<T>(IReadOnlyList<T> items, string list) =>
		_expanded.Contains(list) ? items : items.Take(ShownRows);

	private void Expand(string list) => _expanded.Add(list);

	private async Task RunAsync(DockerChangePlan? plan)
	{
		if (plan is not null)
		{
			await RunPlanAsync(plan, RefreshAsync);
		}
	}
}
