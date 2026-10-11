using System.Globalization;
using Microsoft.AspNetCore.Components;
using Mokaterm.Abstractions.Containers;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.UI.Common.Formatting;
using Mokaterm.UI.Common.Platform;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// The host's images, one line each: pull one by reference, remove a tag or an untagged image (forcing it from the
/// row's menu only), and prune the dangling ones with a confirmation that says how much they hold.
/// </summary>
public sealed partial class DockerImagesPage : DockerPageBase
{
	private IReadOnlyList<DockerImage> _images = [];
	private IReadOnlyList<DockerImage> _shown = [];
	private IReadOnlyList<DockerDiskUsage> _usage = [];
	private string _query = "";
	private string _pull = "";
	private string? _pullError;
	private bool _pulling;

	[Inject]
	private IClipboardService Clipboard { get; set; } = default!;

	private IReadOnlyList<DockerImage> Shown => _shown;

	private int DanglingCount => _images.Count(image => image.IsDangling);

	protected override Task OnInitializedAsync() => RefreshAsync();

	private Task RefreshAsync() => LoadAsync(async (client, token) =>
	{
		Task<IReadOnlyList<DockerImage>> images = client.ListImagesAsync(token);
		Task<IReadOnlyList<DockerDiskUsage>> usage = client.DiskUsageAsync(token);
		await Task.WhenAll(images, usage);
		_images = await images;
		_usage = await usage;
		ApplyFilter();
	});

	private void ApplyFilter() =>
		_shown = [.. _images
			.Where(image => image.Matches(_query))
			.OrderBy(image => image.IsDangling)
			.ThenBy(image => image.Reference, StringComparer.OrdinalIgnoreCase)];

	private void OnQueryChanged(string value)
	{
		_query = value ?? "";
		ApplyFilter();
	}

	private void TogglePull() => _pulling = !_pulling;

	private void OnPullChanged(string? value)
	{
		_pull = value ?? "";
		_pullError = null;
	}

	private async Task PullAsync()
	{
		string reference = _pull.Trim();
		if (DockerNames.ImageProblem(reference) is { } problem)
		{
			_pullError = problem;
			return;
		}

		DockerOutput? output = await RunChangeAsync(DockerCommands.Pull(reference), $"Pull {reference}", afterwards: RefreshAsync);
		if (output is { Succeeded: true })
		{
			_pull = "";
			_pulling = false;
		}
	}

	private Task RemoveAsync(DockerImage image, bool force)
	{
		string message = image.IsDangling || image.RemoveTarget == image.Id
			? $"The image {image.ShortId} is deleted, with every layer nothing else uses."
			: $"The tag {image.Reference} is removed; the image itself goes too when no other tag points at it.";
		if (force)
		{
			message += " Forced: it goes even when containers were created from it or other tags point at it.";
		}

		return ChangeAsync(DockerCommands.RemoveImage(image.RemoveTarget, force), $"Remove {image.Reference}", message, "Remove", RefreshAsync);
	}

	private Task PruneAsync()
	{
		List<DockerImage> dangling = [.. _images.Where(image => image.IsDangling)];
		long listed = dangling.Sum(image => image.SizeBytes ?? 0);
		string reclaimable = _usage.FirstOrDefault(row => row.Type == "Images")?.Reclaimable ?? "";
		string message = string.Create(
			CultureInfo.CurrentCulture,
			$"Removes the {dangling.Count} dangling {(dangling.Count == 1 ? "image" : "images")}: no tag points at them and no container uses them. Their listed sizes add up to {DisplayFormat.Bytes(listed)}, layers they share with other images included, so less may come back.")
			+ (reclaimable.Length > 0 ? $" docker system df counts {reclaimable} reclaimable across every unused image, tagged ones included, which this keeps." : "");

		return ChangeAsync(DockerCommands.PruneDanglingImages(), "Prune dangling images", message, "Prune", RefreshAsync);
	}
}
