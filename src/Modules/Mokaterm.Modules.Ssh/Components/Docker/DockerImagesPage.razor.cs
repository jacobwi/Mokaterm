using Microsoft.AspNetCore.Components;
using Mokaterm.Abstractions.Containers;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.UI.Common.Platform;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// The host's images, one line each: pull one by reference, ask the registries whether a tag moved on (and pull the
/// newer image), start a new container from one, and remove a tag or an untagged image (forcing it from the row's menu
/// only). Pruning lives on the cleanup page, which lists what would go first.
/// </summary>
public sealed partial class DockerImagesPage : DockerManagerPageBase
{
	private IReadOnlyList<DockerImage> _images = [];
	private IReadOnlyList<DockerImage> _shown = [];
	private IReadOnlyList<string> _tagged = [];
	private string _query = "";
	private string _pull = "";
	private string? _pullError;
	private bool _pulling;

	[Inject]
	private IClipboardService Clipboard { get; set; } = default!;

	private IReadOnlyList<DockerImage> Shown => _shown;

	/// <summary>Every tag on the host: what "Check for updates" asks about.</summary>
	private IReadOnlyList<string> TaggedReferences => _tagged;

	private int DanglingCount => _images.Count(image => image.IsDangling);

	protected override Task OnInitializedAsync() => RefreshAsync();

	private Task RefreshAsync() => LoadAsync(async (client, token) =>
	{
		_images = await client.ListImagesAsync(token);
		_tagged = [.. _images.Where(image => image.IsTagged).Select(image => image.Reference).Distinct(StringComparer.Ordinal)];
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
			Feature?.ImageUpdates.Forget([reference]);
			_pull = "";
			_pulling = false;
		}
	}

	/// <summary>
	/// Pulls the newer image for a tag. Containers keep the image they were made from, so the question says how each
	/// kind gets the new one rather than recreating anything itself.
	/// </summary>
	private async Task PullUpdateAsync(DockerImage image)
	{
		DockerOutput? output = await RunChangeAsync(
			DockerCommands.Pull(image.Reference),
			$"Pull the newer {image.Reference}",
			$"The newer {image.Reference} is pulled and the tag moves to it. Containers made from the old one keep running it until they are made again: a compose service through Update in its project, any other container with the docker run it was first started with.",
			"Pull",
			RefreshAsync,
			destructive: false);
		if (output is { Succeeded: true })
		{
			Feature?.ImageUpdates.Forget([image.Reference]);
		}
	}

	private async Task CheckOneAsync(DockerImage image)
	{
		if (Feature is not { } feature || Client is not { } client)
		{
			return;
		}

		try
		{
			IReadOnlyList<ImageUpdateResult> results = await feature.ImageUpdates.CheckAsync(client, [image.Reference], force: true, progress: null, Lifetime);
			if (results is [{ } result])
			{
				Interaction.Notify(result.HasUpdate ? NoticeSeverity.Info : NoticeSeverity.Success, $"{image.Reference}: {result.Detail}");
			}
		}
		catch (Exception ex) when (Describe(ex) is { } problem)
		{
			Interaction.Notify(NoticeSeverity.Error, $"Could not check {image.Reference}: {problem}");
		}
		catch (OperationCanceledException)
		{
			// The page went away.
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
}
