using Microsoft.AspNetCore.Components;
using Mokaterm.Abstractions.Containers;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.UI.Common.Platform;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// The host's images, one line each: pull one by reference, ask the registries whether a tag moved on (and pull the
/// newer image), start a new container from one, and remove a tag or an untagged image (forcing it from the row's menu
/// only). A selected image opens its panel, where it is tagged and pushed; Build opens the build form. Builds and
/// pushes run in the session's background and show their output in place of the list. Pruning lives on the cleanup
/// page, which lists what would go first.
/// </summary>
public sealed partial class DockerImagesPage : DockerManagerPageBase
{
	private const string ListView = "list";

	private const string BuildView = "build";

	private const string LiveView = "live";

	private DockerLiveRuns? _watched;
	private DockerLiveRun? _seenRun;
	private bool _seenRunning;
	private string _view = ListView;
	private string? _selected;
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

	/// <summary>The session's build or push started from this page, running or the last one until dismissed.</summary>
	private DockerLiveRun? LiveRun => LiveRuns?.Current is { Owner: DockerLivePlan.ImagesOwner } run ? run : null;

	private DockerImage? SelectedImage => _selected is { } key ? _images.FirstOrDefault(image => Key(image) == key) : null;

	protected override Task OnInitializedAsync()
	{
		if (Navigation?.TakeBuildImage() == true)
		{
			_view = BuildView;
		}
		else if (LiveRun is { IsRunning: true })
		{
			_view = LiveView;
		}

		Watch();
		return RefreshAsync();
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing && _watched is { } watched)
		{
			watched.Changed -= OnRunsChanged;
			_watched = null;
		}

		base.Dispose(disposing);
	}

	private static string Key(DockerImage image) => image.Reference + "|" + image.Id;

	private static string PanelDetails(DockerImage image) =>
		string.Join(", ", new[] { image.Size, image.CreatedSince }.Where(part => part.Length > 0));

	private IReadOnlyList<string> RepositoryTags(DockerImage image) =>
		image.IsTagged
			? [.. _images.Where(other => other.IsTagged && other.Repository == image.Repository).Select(other => other.Reference).Distinct(StringComparer.Ordinal)]
			: [];

	private void Select(DockerImage image) => _selected = Key(image);

	private void ClosePanel() => _selected = null;

	private void ShowBuild() => _view = BuildView;

	private void ShowLive() => _view = LiveView;

	private void ShowList() => _view = ListView;

	/// <summary>Follows the session's runs, so the list is read again once a build or a push ended.</summary>
	private void Watch()
	{
		_watched = LiveRuns;
		if (_watched is not null)
		{
			_watched.Changed += OnRunsChanged;
			_seenRun = LiveRun;
			_seenRunning = _seenRun?.IsRunning == true;
		}
	}

	private void OnRunsChanged() => _ = InvokeAsync(async () =>
	{
		if (IsDisposed)
		{
			return;
		}

		DockerLiveRun? run = LiveRun;
		bool ended = run is not null && ReferenceEquals(run, _seenRun) && _seenRunning && !run.IsRunning;
		_seenRun = run;
		_seenRunning = run?.IsRunning == true;
		if (ended)
		{
			await RefreshAsync();
		}

		StateHasChanged();
	});

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
