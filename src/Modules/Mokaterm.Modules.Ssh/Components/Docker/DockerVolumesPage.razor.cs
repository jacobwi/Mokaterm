using System.Globalization;
using Microsoft.AspNetCore.Components;
using Moka.Red.ContextMenu;
using Moka.Red.Icons;
using Mokaterm.Abstractions.Containers;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.UI.Common.Formatting;
using Mokaterm.UI.Common.Platform;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// The host's volumes, each with the containers that mount it (running or stopped) and its size beside a bar of its share
/// of the largest. A volume opens its panel: the full path on the docker host, the containers that mount it and where,
/// and a read-only look at its files, which usually takes root. Only unused volumes can be removed, one here, several
/// picked together, or on the cleanup page, and nothing is ever written into one from here. Sizes come from the
/// session's last <c>docker system df --verbose</c>, measured when the page opens on one older than a few minutes and
/// when asked, never with the list.
/// </summary>
public sealed partial class DockerVolumesPage : DockerManagerPageBase
{
	private IReadOnlyList<DockerVolume> _volumes = [];
	private IReadOnlyList<DockerVolume> _shown = [];
	private DockerVolumeDetails? _details;
	private string? _detailsProblem;
	private string? _selected;
	private string _query = "";
	private string _name = "";
	private string? _nameError;
	private bool _creating;
	private bool _measuring;
	private string? _sizeProblem;
	private IReadOnlyDictionary<string, IReadOnlyList<string>> _users = new Dictionary<string, IReadOnlyList<string>>();
	private DockerSystemUsage? _sizesFrom;
	private Dictionary<string, long> _sizes = new(StringComparer.Ordinal);
	private string? _archiveMode;
	private string? _archiveVolume;
	private int _archiveKey;
	private DockerLiveRuns? _watchedRuns;
	private DockerLiveRun? _seenRun;
	private bool _seenRunning;

	[Inject]
	private IClipboardService Clipboard { get; set; } = default!;

	private int UnusedCount => _volumes.Count(CanRemove);

	private DockerVolume? SelectedVolume => _selected is { } name ? _volumes.FirstOrDefault(volume => volume.Name == name) : null;

	private string? Summary => IsFirstLoad
		? null
		: string.Create(CultureInfo.CurrentCulture, $"{_volumes.Count} volumes, {_volumes.Count(volume => volume.InUse)} in use, {_volumes.Count(volume => !volume.InUse)} unused");

	private string ShownText => _shown.Count == _volumes.Count
		? string.Create(CultureInfo.CurrentCulture, $"{_volumes.Count} volumes")
		: string.Create(CultureInfo.CurrentCulture, $"{_shown.Count} of {_volumes.Count} volumes");

	/// <summary>What the rows shown hold together, or null before any of them was measured.</summary>
	private long? ShownBytes => _shown.Select(SizeOf).OfType<long>().ToList() is { Count: > 0 } known ? known.Sum() : null;

	private string MeasuredText => _sizeProblem is { } problem
		? $"Could not measure: {problem}"
		: Feature?.Usage is { ReadAt: { } readAt } usage
			? usage.Usage is null ? "This docker does not size its volumes" : $"Measured {DisplayFormat.Relative(readAt, Time.GetUtcNow())}"
			: "Not measured yet";

	private long LargestSize => _shown.Select(SizeOf).Max() ?? 0;

	protected override async Task OnInitializedAsync()
	{
		WatchRuns();
		await RefreshAsync();
		await MeasureAsync(force: false);
	}

	/// <summary>Shortens the 64 hex digits of an anonymous volume to the 12 docker shows for ids; its title keeps them all.</summary>
	private static string ShownName(DockerVolume volume) =>
		volume.IsAnonymous && volume.Name.Length > 12 ? volume.Name[..12] + "\u2026" : volume.Name;

	// The session's backup or restore, running or the last one until dismissed.
	private DockerLiveRun? VolumeRun => LiveRuns?.Current is { Owner: DockerLivePlan.VolumesOwner } run ? run : null;

	private IReadOnlyList<string> VolumeNames => [.. _volumes.Select(volume => volume.Name)];

	private string ArchiveTitle => _archiveMode switch
	{
		DockerVolumeArchiveView.BackupMode => $"Back up {_archiveVolume}",
		DockerVolumeArchiveView.RestoreMode => _archiveVolume is { } volume ? $"Restore into {volume}" : "Restore a backup",
		_ => VolumeRun?.Title ?? "Backup and restore",
	};

	protected override void Dispose(bool disposing)
	{
		if (disposing && _watchedRuns is { } watched)
		{
			watched.Changed -= OnRunsChanged;
			_watchedRuns = null;
		}

		base.Dispose(disposing);
	}

	// docker cp names the volume inside --mount, which only a name this app passes to docker can sit in.
	private static bool CanArchive(DockerVolume volume) => DockerNames.ExistingNameProblem(volume.Name, "volume") is null;

	private void OpenArchive(string mode, string? volume)
	{
		_archiveMode = mode;
		_archiveVolume = volume;
		_archiveKey++;
	}

	private void CloseArchive() => _archiveMode = null;

	private void OnArchiveOpenChanged(bool open)
	{
		if (!open)
		{
			CloseArchive();
		}
	}

	// A restore that ended may have made a volume, so the list is read again.
	private void WatchRuns()
	{
		_watchedRuns = LiveRuns;
		if (_watchedRuns is not null)
		{
			_watchedRuns.Changed += OnRunsChanged;
			_seenRun = VolumeRun;
			_seenRunning = _seenRun?.IsRunning == true;
		}
	}

	private void OnRunsChanged() => _ = InvokeAsync(async () =>
	{
		if (IsDisposed)
		{
			return;
		}

		DockerLiveRun? run = VolumeRun;
		bool ended = run is not null && ReferenceEquals(run, _seenRun) && _seenRunning && !run.IsRunning;
		_seenRun = run;
		_seenRunning = run?.IsRunning == true;
		if (ended)
		{
			await RefreshAsync();
		}

		StateHasChanged();
	});

	/// <summary>Unused, and named in a way this app passes to docker: a plugin driver may name its volumes otherwise.</summary>
	private static bool CanRemove(DockerVolume volume) =>
		!volume.InUse && DockerNames.ExistingNameProblem(volume.Name, "volume") is null;

	private static int ByName(DockerVolume left, DockerVolume right) => StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);

	private int ByUse(DockerVolume left, DockerVolume right)
	{
		int byUse = right.InUse.CompareTo(left.InUse);
		return byUse != 0 ? byUse : UsersOf(left).Count.CompareTo(UsersOf(right).Count);
	}

	private int BySize(DockerVolume left, DockerVolume right) => Nullable.Compare(SizeOf(left), SizeOf(right));

	private IReadOnlyList<string> UsersOf(DockerVolume volume) => _users.TryGetValue(volume.Name, out IReadOnlyList<string>? users) ? users : [];

	private long? SizeOf(DockerVolume volume)
	{
		DockerSystemUsage? usage = Feature?.Usage.Usage;
		if (!ReferenceEquals(usage, _sizesFrom))
		{
			_sizesFrom = usage;
			_sizes = usage is null
				? new Dictionary<string, long>(StringComparer.Ordinal)
				: usage.Volumes.Where(measured => measured.SizeBytes is not null).ToDictionary(measured => measured.Name, measured => measured.SizeBytes!.Value, StringComparer.Ordinal);
		}

		return _sizes.TryGetValue(volume.Name, out long size) ? size : null;
	}

	/// <summary>The row's menu, from its button or a right-click.</summary>
	private List<MokaContextMenuItem> MenuFor(DockerVolume volume) =>
	[
		new MokaContextMenuItem { Text = "Show details", Icon = MokaIcons.Toggle.Eye, OnClick = () => Select(volume) },
		new MokaContextMenuItem
		{
			Text = "Back up",
			Icon = DockerIcons.Backup,
			Disabled = IsWorking || !CanArchive(volume),
			DividerBefore = true,
			OnClickSync = () => OpenArchive(DockerVolumeArchiveView.BackupMode, volume.Name),
		},
		new MokaContextMenuItem
		{
			Text = "Restore a backup into it",
			Icon = DockerIcons.Restore,
			Disabled = IsWorking || !CanArchive(volume),
			OnClickSync = () => OpenArchive(DockerVolumeArchiveView.RestoreMode, volume.Name),
		},
		new MokaContextMenuItem { Text = "Copy name", Icon = MokaIcons.Content.Copy, DividerBefore = true, OnClick = () => CopyTextAsync(Clipboard, volume.Name, "name") },
		new MokaContextMenuItem
		{
			Text = "Copy the host path",
			Icon = MokaIcons.Content.Copy,
			Disabled = volume.Mountpoint.Length == 0,
			OnClick = () => CopyTextAsync(Clipboard, volume.Mountpoint, "host path"),
		},
		new MokaContextMenuItem { Text = "Remove", Icon = MokaIcons.Action.Delete, Disabled = IsBusy || !CanRemove(volume), DividerBefore = true, OnClick = () => RemoveAsync(volume) },
	];

	private Task OnRowClick(DockerVolume volume)
	{
		if (volume.Name == _selected)
		{
			ClosePanel();
			return Task.CompletedTask;
		}

		return Select(volume);
	}

	private Task RefreshAsync() => LoadAsync(async (client, token) =>
	{
		Task<IReadOnlyList<DockerVolume>> volumes = client.ListVolumesAsync(token);
		Task<IReadOnlyList<DockerContainer>> containers = client.ListContainersAsync(token);
		await Task.WhenAll(volumes, containers);
		_volumes = await volumes;
		_users = DockerUsers.OfVolumes(await containers);
		if (_selected is { } selected && !_volumes.Any(volume => volume.Name == selected))
		{
			ClosePanel();
		}

		ApplyFilter();
	});

	private void ApplyFilter() =>
		_shown = [.. _volumes
			.Where(volume => volume.Matches(_query))
			.OrderBy(volume => !volume.InUse)
			.ThenBy(volume => volume.Name, StringComparer.OrdinalIgnoreCase)];

	private void OnQueryChanged(string value)
	{
		_query = value ?? "";
		ApplyFilter();
	}

	private void ToggleCreate() => _creating = !_creating;

	/// <summary>
	/// Reads the sizes of every volume, unless the session's last read is recent enough and the user did not ask. Its
	/// failure stays in the foot: the list itself is fine without sizes.
	/// </summary>
	private async Task MeasureAsync(bool force)
	{
		if (_measuring || Feature is not { } feature || Client is not { } client || (!force && !feature.Usage.IsStale))
		{
			return;
		}

		_measuring = true;
		StateHasChanged();
		try
		{
			await feature.Usage.MeasureAsync(client, Lifetime);
			_sizeProblem = null;
		}
		catch (Exception ex) when (Describe(ex) is { } problem)
		{
			_sizeProblem = problem;
		}
		catch (OperationCanceledException)
		{
			return;
		}
		finally
		{
			_measuring = false;
		}

		if (!IsDisposed)
		{
			StateHasChanged();
		}
	}

	private void OnNameChanged(string? value)
	{
		_name = value ?? "";
		_nameError = null;
	}

	private async Task Select(DockerVolume volume)
	{
		_selected = volume.Name;
		_details = null;
		_detailsProblem = DockerNames.ExistingNameProblem(volume.Name, "volume");
		if (Client is not { } client || _detailsProblem is not null)
		{
			return;
		}

		try
		{
			_details = await client.VolumeDetailsAsync(volume.Name, Lifetime);
		}
		catch (Exception ex) when (Describe(ex) is { } problem)
		{
			_detailsProblem = problem;
		}
		catch (OperationCanceledException)
		{
			return;
		}
	}

	private void ClosePanel()
	{
		_selected = null;
		_details = null;
		_detailsProblem = null;
	}

	private async Task CreateAsync()
	{
		string name = _name.Trim();
		if (DockerNames.NewNameProblem(name, "volume") is { } problem)
		{
			_nameError = problem;
			return;
		}

		DockerOutput? output = await RunChangeAsync(DockerCommands.CreateVolume(name), $"Create volume {name}", afterwards: RefreshAsync);
		if (output is { Succeeded: true })
		{
			_name = "";
			_creating = false;
		}
	}

	/// <summary>The picked volumes no container uses, in one <c>docker volume rm</c>. One alone goes the way its own row's Remove does.</summary>
	private Task RemoveManyAsync(IReadOnlyList<DockerVolume> picked)
	{
		List<DockerVolume> removable = [.. picked.Where(CanRemove)];
		if (removable.Count == 0)
		{
			return Task.CompletedTask;
		}

		if (removable is [var only] && picked.Count == 1)
		{
			return RemoveAsync(only);
		}

		int kept = picked.Count - removable.Count;
		string count = removable.Count.ToString(CultureInfo.CurrentCulture);
		string keeping = kept switch
		{
			0 => "",
			1 => " The other one picked is in use and stays.",
			_ => string.Create(CultureInfo.CurrentCulture, $" The other {kept} picked are in use and stay."),
		};
		string message = removable is [var one]
			? $"The volume {ShownName(one)} and everything stored in it are deleted. No container uses it now, and this cannot be undone."
			: $"These {count} volumes and everything stored in them are deleted: {DockerWording.NameList([.. removable.Select(ShownName)])}. No container uses them now, and this cannot be undone.";
		return ChangeAsync(
			DockerCommands.RemoveVolumes([.. removable.Select(volume => volume.Name)]),
			removable is [var single] ? $"Remove volume {ShownName(single)}" : $"Remove {count} volumes",
			message + keeping,
			"Remove",
			RefreshAsync);
	}

	private Task RemoveAsync(DockerVolume volume) => ChangeAsync(
		DockerCommands.RemoveVolumes([volume.Name]),
		$"Remove volume {volume.Name}",
		$"The volume {volume.Name} and everything stored in it are deleted. No container uses it now, and this cannot be undone.",
		"Remove",
		RefreshAsync);
}
