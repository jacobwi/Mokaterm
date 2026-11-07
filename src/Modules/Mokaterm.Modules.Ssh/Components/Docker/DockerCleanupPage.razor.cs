using System.Globalization;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.UI.Common.Formatting;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// What Docker keeps on disk and what can go: the summary docker system df prints, then one kind at a time from its
/// verbose listing (stopped containers, dangling or unused images, unused volumes, unused build cache), each list
/// exactly what the page's button removes. The images and volumes pages send their "remove unused" here, so there is
/// one place that prunes.
/// </summary>
public sealed partial class DockerCleanupPage : DockerManagerPageBase
{
	/// <summary>How many rows a list shows before "Show all"; the command always names every one.</summary>
	private const int ShownRows = 200;

	private const string ContainersKind = "containers";
	private const string ImagesKind = "images";
	private const string VolumesKind = "volumes";
	private const string CacheKind = "cache";

	private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);
	private IReadOnlyList<DockerDiskUsage> _summary = [];
	private DockerSystemUsage? _usage;
	private IReadOnlyList<DockerContainer> _containers = [];
	private IReadOnlyList<DockerImage> _images = [];
	private IReadOnlyList<DockerVolume> _volumes = [];
	private IReadOnlyList<DockerBuildCacheEntry> _cache = [];
	private string _kind = ContainersKind;
	private bool _taggedImages;
	private bool _namedVolumes;

	private int DanglingCount => _usage is null ? 0 : DockerCleanupPlans.Images(_usage, tagged: false).Count;

	private int UnusedImageCount => _usage is null ? 0 : DockerCleanupPlans.Images(_usage, tagged: true).Count;

	private int AnonymousCount => _usage is null ? 0 : DockerCleanupPlans.Volumes(_usage, named: false).Count;

	private int UnusedVolumeCount => _usage is null ? 0 : DockerCleanupPlans.Volumes(_usage, named: true).Count;

	private string? Summary => _summary.Count == 0
		? null
		: string.Create(
			CultureInfo.CurrentCulture,
			$"{DisplayFormat.Bytes(_summary.Sum(row => DockerSizes.Parse(row.Size) ?? 0))} on disk, {DisplayFormat.Bytes(_summary.Sum(row => row.ReclaimableBytes ?? 0))} reclaimable");

	private int ShownCount => _kind switch
	{
		ImagesKind => _images.Count,
		VolumesKind => _volumes.Count,
		CacheKind => _cache.Count,
		_ => _containers.Count,
	};

	private string KindTitle => _kind switch
	{
		ImagesKind => _taggedImages ? "Unused images" : "Dangling images",
		VolumesKind => _namedVolumes ? "Unused volumes" : "Unused anonymous volumes",
		CacheKind => "Unused build cache",
		_ => "Stopped containers",
	};

	private string KindNote => _kind switch
	{
		ImagesKind => _taggedImages
			? "Every image no container was made from, tagged or not. Sizes are what each one alone holds; layers shared with a kept image stay."
			: "Images no tag points at and no container uses, usually left behind when a tag moved to a newer build or pull.",
		VolumesKind => "A volume holds a container's data. No container uses these now, but deleting one deletes what it stores, for good.",
		CacheKind => "Layers and build steps no image refers to. The next build that needs one makes it again.",
		_ => "Containers that exited or were never started, with their writable layers.",
	};

	private string ShownTotals => _kind switch
	{
		ImagesKind => Totals(_images.Count, _images.DistinctBy(image => image.Id).Sum(image => image.FreedBytes ?? 0)),
		VolumesKind => Totals(_volumes.Count, _volumes.Sum(volume => volume.SizeBytes ?? 0)),
		CacheKind => Totals(_cache.Count, _cache.Sum(entry => entry.SizeBytes ?? 0)),
		_ => Totals(_containers.Count, _containers.Sum(container => DockerSizes.Parse(container.Row.Size) ?? 0)),
	};

	private string RemoveText => _kind switch
	{
		ImagesKind => string.Create(CultureInfo.CurrentCulture, $"Remove {_images.Count} {(_images.Count == 1 ? "image" : "images")}"),
		VolumesKind => string.Create(CultureInfo.CurrentCulture, $"Delete {_volumes.Count} {(_volumes.Count == 1 ? "volume" : "volumes")} and their data"),
		CacheKind => "Clear the build cache",
		_ => string.Create(CultureInfo.CurrentCulture, $"Remove {_containers.Count} {(_containers.Count == 1 ? "container" : "containers")}"),
	};

	protected override async Task OnInitializedAsync()
	{
		string? asked = Navigation?.TakeCleanupKind();
		await RefreshAsync();
		_kind = asked is ContainersKind or ImagesKind or VolumesKind or CacheKind ? asked : FirstWithCandidates();
	}

	private static string Totals(int count, long bytes) => count == 0
		? ""
		: string.Create(CultureInfo.CurrentCulture, $"{count} · {DisplayFormat.Bytes(bytes)}");

	/// <summary>The first kind that has something to go, so the page opens where there is work.</summary>
	private string FirstWithCandidates() =>
		_containers.Count > 0 ? ContainersKind
		: _images.Count > 0 ? ImagesKind
		: _volumes.Count > 0 ? VolumesKind
		: _cache.Count > 0 ? CacheKind
		: ContainersKind;

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

	private void OnKindChanged(string? value) => _kind = value ?? ContainersKind;

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

	private IReadOnlyList<T> Capped<T>(IReadOnlyList<T> items) =>
		_expanded.Contains(_kind) || items.Count <= ShownRows ? items : [.. items.Take(ShownRows)];

	private async Task RemoveAsync()
	{
		DockerChangePlan? plan = _kind switch
		{
			ImagesKind => DockerCleanupPlans.RemoveImages(_images, _taggedImages),
			VolumesKind => DockerCleanupPlans.RemoveVolumes(_volumes),
			CacheKind => DockerCleanupPlans.ClearBuildCache(_cache),
			_ => DockerCleanupPlans.RemoveContainers(_containers),
		};

		if (plan is not null)
		{
			await RunPlanAsync(plan, RefreshAsync);
		}
	}
}
