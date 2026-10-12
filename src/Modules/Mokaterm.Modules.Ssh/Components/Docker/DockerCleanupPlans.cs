using System.Globalization;
using Mokaterm.Abstractions.Containers;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.UI.Common.Formatting;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// What each cleanup removes, read from <c>docker system df --verbose</c>, and the change that removes exactly that:
/// the cleanup names every container, image and volume on its command line, so nothing goes that the preview did not
/// list. Build cache has no names to give, so clearing it is <c>builder prune</c>, which takes what the preview lists.
/// </summary>
internal static class DockerCleanupPlans
{
	/// <summary>Images no container was made from: only those no tag points at, or every one when <paramref name="tagged"/> is set.</summary>
	public static IReadOnlyList<DockerImage> Images(DockerSystemUsage usage, bool tagged)
	{
		ArgumentNullException.ThrowIfNull(usage);

		// An image whose containers docker did not count is kept, so nothing goes on a guess.
		return [.. usage.Images
			.Where(image => image.ContainerCount == 0 && (tagged || image.IsDangling))
			.OrderByDescending(image => image.FreedBytes ?? 0)];
	}

	/// <summary>Exited, created and dead containers: what <c>container prune</c> would take.</summary>
	public static IReadOnlyList<DockerContainer> StoppedContainers(DockerSystemUsage usage)
	{
		ArgumentNullException.ThrowIfNull(usage);
		return [.. usage.Containers
			.Where(container => container.State is "exited" or "created" or "dead")
			.OrderBy(container => container.Name, StringComparer.OrdinalIgnoreCase)];
	}

	/// <summary>Volumes no container refers to: only anonymous ones, or named ones too when <paramref name="named"/> is set.</summary>
	public static IReadOnlyList<DockerVolume> Volumes(DockerSystemUsage usage, bool named)
	{
		ArgumentNullException.ThrowIfNull(usage);
		return [.. usage.Volumes
			.Where(volume => !volume.InUse && (named || volume.IsAnonymous) && DockerNames.ExistingNameProblem(volume.Name, "volume") is null)
			.OrderByDescending(volume => volume.SizeBytes ?? 0)];
	}

	/// <summary>Build cache entries no running build holds.</summary>
	public static IReadOnlyList<DockerBuildCacheEntry> BuildCache(DockerSystemUsage usage)
	{
		ArgumentNullException.ThrowIfNull(usage);
		return [.. usage.BuildCache.Where(entry => !entry.InUse).OrderByDescending(entry => entry.SizeBytes ?? 0)];
	}

	public static DockerChangePlan? RemoveContainers(IReadOnlyList<DockerContainer> containers)
	{
		ArgumentNullException.ThrowIfNull(containers);
		if (containers.Count == 0)
		{
			return null;
		}

		List<string> names = [.. containers.Select(container => container.Name)];
		return new DockerChangePlan(
			DockerCommands.RemoveContainers(names),
			"Remove stopped containers",
			string.Create(CultureInfo.CurrentCulture, $"Removes {Count(names.Count, "stopped container")} and what was written inside: {DockerWording.NameList(names)}. Named volumes stay, and this cannot be undone."),
			"Remove");
	}

	/// <param name="tagged">True for unused images with tags among them, which says so in the question.</param>
	public static DockerChangePlan? RemoveImages(IReadOnlyList<DockerImage> images, bool tagged)
	{
		ArgumentNullException.ThrowIfNull(images);
		if (images.Count == 0)
		{
			return null;
		}

		List<string> targets = [.. images.Select(image => image.RemoveTarget).Distinct(StringComparer.Ordinal)];
		string freed = DisplayFormat.Bytes(images.DistinctBy(image => image.Id).Sum(image => image.FreedBytes ?? 0));
		string message = tagged
			? $"Deletes {Count(images.Count, "image")} that no container uses, tagged ones included: {DockerWording.NameList([.. images.Select(image => image.Reference)])}. About {freed} comes back, and a pull or a build brings an image back."
			: $"Deletes {Count(images.Count, "dangling image")} that no tag points at and no container uses. About {freed} comes back.";
		return new DockerChangePlan(DockerCommands.RemoveImages(targets), tagged ? "Remove unused images" : "Remove dangling images", message, "Remove");
	}

	public static DockerChangePlan? RemoveVolumes(IReadOnlyList<DockerVolume> volumes)
	{
		ArgumentNullException.ThrowIfNull(volumes);
		if (volumes.Count == 0)
		{
			return null;
		}

		List<string> names = [.. volumes.Select(volume => volume.Name)];
		string size = DisplayFormat.Bytes(volumes.Sum(volume => volume.SizeBytes ?? 0));
		return new DockerChangePlan(
			DockerCommands.RemoveVolumes(names),
			"Remove unused volumes",
			string.Create(CultureInfo.CurrentCulture, $"Deletes {Count(names.Count, "volume")} and everything stored there, about {size}: {DockerWording.NameList(names)}. No container uses them now, but the data is lost for good."),
			"Delete the data");
	}

	public static DockerChangePlan? ClearBuildCache(IReadOnlyList<DockerBuildCacheEntry> entries)
	{
		ArgumentNullException.ThrowIfNull(entries);
		if (entries.Count == 0)
		{
			return null;
		}

		string size = DisplayFormat.Bytes(entries.Sum(entry => entry.SizeBytes ?? 0));
		return new DockerChangePlan(
			DockerCommands.PruneBuildCache(),
			"Clear the build cache",
			string.Create(CultureInfo.CurrentCulture, $"Deletes every build cache entry nothing uses: {Count(entries.Count, "entry", "entries")}, about {size}. The next build runs those steps again."),
			"Clear");
	}

	private static string Count(int count, string one, string? many = null) => count == 1
		? $"1 {one}"
		: string.Create(CultureInfo.CurrentCulture, $"{count} {many ?? one + "s"}");
}
