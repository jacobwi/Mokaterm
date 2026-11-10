using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Mokaterm.Abstractions.Containers;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.UI.Common.Files;

namespace Mokaterm.Modules.Ssh.Components.Docker;

// The body of the volumes page's backup and restore dialog: the form for one, then the run's output once it started,
// with Download for a backup that finished. The run belongs to the session, so closing the dialog leaves it running.
public sealed partial class DockerVolumeArchiveView : DockerManagerPageBase
{
	public const string BackupMode = "backup";

	public const string RestoreMode = "restore";

	public const string RunMode = "run";

	private const string FolderField = "folder";
	private const string NameField = "name";
	private const string ArchiveField = "archive";
	private const string VolumeField = "volume";
	private const string ImageField = "image";

	private Dictionary<string, string> _problems = new(StringComparer.Ordinal);
	private IReadOnlyList<DockerImage>? _images;
	private DockerImage? _helper;
	private DockerVolumeDetails? _details;
	private IReadOnlyList<RemoteFileEntry>? _found;
	private string _mode = BackupMode;
	private string _folder = "";
	private string _name = "";
	private string _archive = "";
	private string _newName = "";
	private string _foundFolder = "";
	private string? _listProblem;
	private bool _gzip = true;
	private bool _newVolume;
	private bool _listing;

	[Parameter]
	public string Mode { get; set; } = BackupMode;

	// The volume backed up or restored into; null restores into a new one.
	[Parameter]
	public string? Volume { get; set; }

	// Every volume on the host, so a new one's name is checked against them before docker is asked.
	[Parameter]
	public IReadOnlyList<string> TakenNames { get; set; } = [];

	[Parameter]
	public EventCallback OnClose { get; set; }

	// Optional on purpose: a host without the file browser still backs up, and only Download is left out.
	[Inject]
	private IServiceProvider Services { get; set; } = default!;

	private IReadOnlyList<DockerVolumeUse> RunningUsers =>
		_details is { } details ? [.. details.UsedBy.Where(use => use.State is "running" or "paused" or "restarting")] : [];

	protected override Task OnInitializedAsync()
	{
		_mode = Mode;
		_newVolume = Volume is null;
		_folder = HostFiles?.StagingDirectory ?? "/tmp";
		_name = Volume is { } volume ? DockerVolumeArchive.DefaultFileName(volume, Time.GetUtcNow(), gzip: true) : "";
		return _mode == RunMode ? Task.CompletedTask : LoadAsync();
	}

	private static string Names(IReadOnlyList<DockerVolumeUse> uses) => string.Join(", ", uses.Select(use => use.Container).Distinct(StringComparer.Ordinal));

	private static string HelperName(DockerImage image) => image.IsTagged ? image.Reference : "the image " + image.ShortId;

	private static string SizeOf(DockerImage image) => image.Size.Length > 0 ? $" ({image.Size})" : "";

	private Task LoadAsync() => LoadAsync(async (client, token) =>
	{
		_images = await client.ListImagesAsync(token);
		_helper = DockerVolumeArchive.PickHelperImage(_images);
		_details = Volume is { } volume && DockerNames.ExistingNameProblem(volume, "volume") is null
			? await client.VolumeDetailsAsync(volume, token)
			: null;
	});

	private string? Problem(string field) => _problems.TryGetValue(field, out string? problem) ? problem : null;

	private void Set(ref string field, string? value, string problem)
	{
		field = value ?? "";
		if (_problems.ContainsKey(problem))
		{
			_problems = _problems.Where(entry => entry.Key != problem).ToDictionary(StringComparer.Ordinal);
		}
	}

	private void OnNameChanged(string? value)
	{
		Set(ref _name, value, NameField);
		if (DockerVolumeArchive.IsGzipName(_name.Trim()))
		{
			_gzip = true;
		}
		else if (_name.Trim().EndsWith(".tar", StringComparison.OrdinalIgnoreCase))
		{
			_gzip = false;
		}
	}

	// Picking the other kind swaps the name's ending, so the name always says what the file holds.
	private void OnCompressionChanged(string? value)
	{
		_gzip = value == "gzip";
		string name = _name.Trim();
		string stem = name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ? name[..^7]
			: name.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase) ? name[..^4]
			: name.EndsWith(".tar", StringComparison.OrdinalIgnoreCase) ? name[..^4]
			: name;
		_name = stem + (_gzip ? ".tar.gz" : ".tar");
	}

	private void OnTargetChanged(string? value) => _newVolume = value == "new";

	private void PickArchive(RemoteFileEntry entry) => Set(ref _archive, entry.Path, ArchiveField);

	private async Task PickBackupFolderAsync()
	{
		if (await PickHostFolderAsync("Folder for the backup", _folder) is { } folder)
		{
			Set(ref _folder, folder, FolderField);
		}
	}

	// The browser picks a folder and the archives in it are listed to choose from: it has no way to pick a file.
	private async Task PickArchiveFolderAsync()
	{
		string start = _archive.Trim() is { Length: > 0 } typed && typed.StartsWith('/') ? RemotePath.GetParent(typed) : _folder;
		if (await PickHostFolderAsync("Folder that holds the archive", start) is not { } folder || HostFiles is not { } files)
		{
			return;
		}

		_listing = true;
		_foundFolder = folder;
		_listProblem = null;
		try
		{
			IReadOnlyList<RemoteFileEntry> entries = await files.ListAsync(folder, asRoot: false, Lifetime);
			_found = [.. entries
				.Where(entry => entry.Kind == RemoteEntryKind.File && DockerVolumeArchive.IsRestorable(entry.Name))
				.OrderByDescending(entry => entry.LastModified)];
		}
		catch (Exception ex) when (Describe(ex) is { } problem)
		{
			_found = [];
			_listProblem = problem;
		}
		catch (OperationCanceledException)
		{
			return;
		}
		finally
		{
			_listing = false;
		}
	}

	private async Task PullBusyboxAsync()
	{
		DockerOutput? output = await RunChangeAsync(DockerCommands.Pull("busybox"), "Pull busybox", afterwards: LoadAsync, destructive: false);
		if (output is { Succeeded: true })
		{
			_problems = _problems.Where(entry => entry.Key != ImageField).ToDictionary(StringComparer.Ordinal);
		}
	}

	private async Task BackUpAsync()
	{
		if (Volume is not { } volume)
		{
			return;
		}

		string folder = _folder.Trim();
		string name = _name.Trim();
		Dictionary<string, string> problems = new(StringComparer.Ordinal);
		if (DockerNames.PathProblem(folder, "folder") is { } folderProblem)
		{
			problems[FolderField] = folderProblem;
		}

		if (DockerVolumeArchive.BackupNameProblem(name) is { } nameProblem)
		{
			problems[NameField] = nameProblem;
		}

		_problems = problems;
		if (problems.Count > 0 || _helper is not { } helper)
		{
			return;
		}

		string target = RemotePath.Combine(folder, name);
		string helperName = DockerVolumeArchive.NewHelperName();
		bool gzip = DockerVolumeArchive.IsGzipName(name);
		DockerLivePlan plan = new(
			[DockerVolumeArchive.Backup(volume, helper.Id, target, helperName, DockerVolumeArchive.PartialFile(target), gzip)],
			$"Back up {volume}",
			$"Docker copies everything in {volume} into {target}{(gzip ? ", compressed with gzip" : "")}, through {helperName}, a container made from {HelperName(helper)} that is never started and is removed afterwards. Nothing in the volume changes. The output shows here while it runs, and the server stops it after {DockerTimeLimit.Describe(DockerVolumeArchive.TimeLimit)}.",
			"Back up",
			DockerLivePlan.VolumesOwner,
			ErrorIsProgress: false)
		{
			Cleanup = [DockerVolumeArchive.RemoveHelper(helperName)],
			Produces = target,
			StopNote = "Stopped. The partial file and the helper container are removed; the volume was only read.",
		};

		if (await StartLiveAsync(plan) is not null)
		{
			_mode = RunMode;
		}
	}

	private async Task RestoreAsync()
	{
		string archive = _archive.Trim();
		string volume = _newVolume ? _newName.Trim() : Volume ?? "";
		Dictionary<string, string> problems = new(StringComparer.Ordinal);
		if (DockerNames.PathProblem(archive, "archive") is { } archiveProblem)
		{
			problems[ArchiveField] = archiveProblem;
		}

		if (_newVolume && (DockerNames.NewNameProblem(volume, "volume")
			?? (TakenNames.Contains(volume, StringComparer.Ordinal) ? $"A volume named {volume} exists already." : null)) is { } volumeProblem)
		{
			problems[VolumeField] = volumeProblem;
		}

		_problems = problems;
		if (problems.Count > 0 || _helper is not { } helper || (!_newVolume && RunningUsers.Count > 0))
		{
			return;
		}

		string helperName = DockerVolumeArchive.NewHelperName();
		string confirm = _newVolume
			? $"Docker makes the volume {volume} and unpacks {archive} into it, through {helperName}, a container made from {HelperName(helper)} that is never started and is removed afterwards. Should the restore fail, the new volume is removed again."
			: $"Docker unpacks {archive} into {volume}, through {helperName}, a container made from {HelperName(helper)} that is never started and is removed afterwards. Files in the archive replace the volume's files at the same paths, files it does not hold stay, and this cannot be undone.";
		DockerLivePlan plan = new(
			[DockerVolumeArchive.Restore(volume, _newVolume, helper.Id, archive, helperName)],
			$"Restore into {volume}",
			confirm,
			"Restore",
			DockerLivePlan.VolumesOwner,
			ErrorIsProgress: false)
		{
			Cleanup = [DockerVolumeArchive.RemoveHelper(helperName)],
			StopNote = _newVolume
				? "Stopped. The helper container and the new volume are removed again."
				: "Stopped. The helper container is removed; the files unpacked before the stop stay in the volume.",
			Destructive = !_newVolume,
		};

		if (await StartLiveAsync(plan) is not null)
		{
			_mode = RunMode;
		}
	}

	private async Task DownloadAsync(string path)
	{
		if (Services.GetService<IRemoteFileDownloads>() is not { } downloads
			|| Session.Session?.GetFeature<IFileSystemFeature>() is not { } files
			|| Feature is not { } feature)
		{
			Interaction.Notify(NoticeSeverity.Warning, $"This session cannot download files. The backup is at {path} on {Feature?.Host ?? "the docker host"}.");
			return;
		}

		try
		{
			await downloads.DownloadAsync(new FileBrowserRequest { FileSystem = files, Title = Session.Title, Location = feature.Host }, path, Lifetime);
			Interaction.Notify(NoticeSeverity.Info, $"The download of {RemotePath.GetName(path)} is in the transfer queue.");
		}
		catch (Exception ex) when (Describe(ex) is { } problem)
		{
			Interaction.Notify(NoticeSeverity.Warning, problem, "Could not download the backup");
		}
	}
}
