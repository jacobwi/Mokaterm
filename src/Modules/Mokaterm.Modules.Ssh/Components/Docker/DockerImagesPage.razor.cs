using System.Globalization;
using Microsoft.AspNetCore.Components;
using Moka.Red.ContextMenu;
using Moka.Red.Icons;
using Mokaterm.Abstractions.Containers;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.UI.Common.Formatting;
using Mokaterm.UI.Common.Icons;
using Mokaterm.UI.Common.Platform;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// The host's images in one table, each with the containers made from it and its size beside a bar of its share of the
/// largest: pull one by reference, ask the registries whether a tag moved on (and pull the newer image), start a new
/// container from one, and remove a tag or an untagged image (forcing it from the row's menu only), or several picked
/// together.
/// A selected image opens its panel, where it is tagged and pushed; Build opens the build form. Builds and pushes run in
/// the session's background and show their output in place of the list. Pruning lives on the cleanup page, which lists
/// what would go first.
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
	private Dictionary<string, IReadOnlyList<string>> _users = new(StringComparer.Ordinal);
	private long _largest;
	private string _query = "";
	private string _pull = "";
	private string? _pullError;
	private bool _pulling;

	[Inject]
	private IClipboardService Clipboard { get; set; } = default!;

	private bool ShowsList => _view == ListView;

	private string? Summary => IsFirstLoad
		? null
		: string.Create(
			CultureInfo.CurrentCulture,
			$"{_images.Count} images, {_tagged.Count} tagged, {DanglingCount} dangling · {DisplayFormat.Bytes(TotalBytes(_images))}");

	private string ShownText => _shown.Count == _images.Count
		? string.Create(CultureInfo.CurrentCulture, $"{_images.Count} images")
		: string.Create(CultureInfo.CurrentCulture, $"{_shown.Count} of {_images.Count} images");

	private EventCallback<string> SearchCallback => ShowsList ? EventCallback.Factory.Create<string>(this, OnQueryChanged) : default;

	private EventCallback RefreshCallback => ShowsList ? EventCallback.Factory.Create(this, () => RefreshByHandAsync(RefreshAsync)) : default;

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

	private static int ByReference(DockerImage left, DockerImage right) => StringComparer.OrdinalIgnoreCase.Compare(left.Reference, right.Reference);

	private static int ByCreated(DockerImage left, DockerImage right) => Nullable.Compare(left.CreatedAt, right.CreatedAt);

	private static int BySize(DockerImage left, DockerImage right) => Nullable.Compare(left.SizeBytes, right.SizeBytes);

	/// <summary>Every image once, however many tags point at it.</summary>
	private static long TotalBytes(IEnumerable<DockerImage> images) => images.DistinctBy(image => image.Id).Sum(image => image.SizeBytes ?? 0);

	private int ByUsers(DockerImage left, DockerImage right) => UsersOf(left).Count.CompareTo(UsersOf(right).Count);

	private IReadOnlyList<string> UsersOf(DockerImage image) => _users.TryGetValue(Key(image), out IReadOnlyList<string>? users) ? users : [];

	private ImageUpdateResult? UpdateOf(DockerImage image) => image.IsTagged ? Feature?.ImageUpdates.Find(image.Reference) : null;

	/// <summary>The row's menu, from its button or a right-click.</summary>
	private List<MokaContextMenuItem> MenuFor(DockerImage image)
	{
		List<MokaContextMenuItem> items = [];
		if (UpdateOf(image) is { HasUpdate: true })
		{
			items.Add(new MokaContextMenuItem { Text = "Pull the newer image", Icon = DockerIcons.Update, Disabled = IsBusy, OnClick = () => PullUpdateAsync(image) });
		}

		if (image.IsTagged)
		{
			items.Add(new MokaContextMenuItem { Text = "Check for an update", Icon = DockerIcons.Update, OnClick = () => CheckOneAsync(image) });
		}

		if (Navigation is { } navigation)
		{
			items.Add(new MokaContextMenuItem { Text = "New container from this image", Icon = MokatermIcons.Play, Disabled = IsBusy, OnClickSync = () => navigation.NewContainer(image.Reference) });
		}

		items.Add(new MokaContextMenuItem { Text = "Tag or push", Icon = DockerIcons.Push, OnClickSync = () => Select(image) });
		items.Add(new MokaContextMenuItem { Text = "Remove", Icon = MokaIcons.Action.Delete, Disabled = IsBusy, DividerBefore = true, OnClick = () => RemoveAsync(image, force: false) });
		items.Add(new MokaContextMenuItem { Text = "Remove even if used (--force)", Icon = MokaIcons.Action.Delete, Disabled = IsBusy, OnClick = () => RemoveAsync(image, force: true) });
		items.Add(new MokaContextMenuItem { Text = $"Copy id {image.ShortId}", Icon = MokaIcons.Content.Copy, DividerBefore = true, CssClass = CopyClick.Class, OnClick = () => CopyTextAsync(Clipboard, image.Id, "id") });
		items.Add(new MokaContextMenuItem { Text = "Copy the reference", Icon = MokaIcons.Content.Copy, CssClass = CopyClick.Class, OnClick = () => CopyTextAsync(Clipboard, image.Reference, "reference") });
		return items;
	}

	private IReadOnlyList<string> RepositoryTags(DockerImage image) =>
		image.IsTagged
			? [.. _images.Where(other => other.IsTagged && other.Repository == image.Repository).Select(other => other.Reference).Distinct(StringComparer.Ordinal)]
			: [];

	private void Select(DockerImage image) => _selected = Key(image);

	private void OnRowClick(DockerImage image)
	{
		if (Key(image) == _selected)
		{
			ClosePanel();
			return;
		}

		Select(image);
	}

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
		Task<IReadOnlyList<DockerImage>> images = client.ListImagesAsync(token);
		Task<IReadOnlyList<DockerContainer>> containers = client.ListContainersAsync(token);
		await Task.WhenAll(images, containers);
		_images = await images;
		IReadOnlyList<DockerContainer> listed = await containers;
		_tagged = [.. _images.Where(image => image.IsTagged).Select(image => image.Reference).Distinct(StringComparer.Ordinal)];
		_users = _images.ToDictionary(Key, image => DockerUsers.OfImage(image, listed), StringComparer.Ordinal);
		_largest = _images.Max(image => image.SizeBytes) ?? 0;
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

	/// <summary>
	/// The picked images in one <c>docker image rm</c>, which docker works through one by one: a tag goes on its own, and
	/// an image that a container still uses is refused while the rest go.
	/// </summary>
	private Task RemoveManyAsync(IReadOnlyList<DockerImage> picked)
	{
		if (picked is [var only])
		{
			return RemoveAsync(only, force: false);
		}

		List<string> names = [.. picked.Select(image => image.Reference)];
		string count = picked.Count.ToString(CultureInfo.CurrentCulture);
		return ChangeAsync(
			DockerCommands.RemoveImages([.. picked.Select(image => image.RemoveTarget).Distinct(StringComparer.Ordinal)]),
			$"Remove {count} images",
			$"These {count} images are removed: {DockerWording.NameList(names)}. A tag goes on its own, and an image goes too when no other tag points at it. Docker refuses any that a container was made from, and removes the rest.",
			"Remove",
			RefreshAsync);
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
