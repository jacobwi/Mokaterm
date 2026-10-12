using Mokaterm.Abstractions.Containers;
using Mokaterm.Modules.Ssh.Docker;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// The host's volumes, marked in use unless no container at all (running or stopped) refers to them. A volume opens
/// its panel: the full path on the docker host, the containers that mount it and where, and a read-only look at its
/// files, which usually takes root. Only unused volumes can be removed, one here or together on the cleanup page, and
/// nothing is ever written into one from here.
/// </summary>
public sealed partial class DockerVolumesPage : DockerPageBase
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

	private IReadOnlyList<DockerVolume> Shown => _shown;

	private int UnusedCount => _volumes.Count(CanRemove);

	protected override Task OnInitializedAsync() => RefreshAsync();

	/// <summary>Unused, and named in a way this app passes to docker: a plugin driver may name its volumes otherwise.</summary>
	private static bool CanRemove(DockerVolume volume) =>
		!volume.InUse && DockerNames.ExistingNameProblem(volume.Name, "volume") is null;

	private Task RefreshAsync() => LoadAsync(async (client, token) =>
	{
		_volumes = await client.ListVolumesAsync(token);
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

	private Task RemoveAsync(DockerVolume volume) => ChangeAsync(
		DockerCommands.RemoveVolumes([volume.Name]),
		$"Remove volume {volume.Name}",
		$"The volume {volume.Name} and everything stored in it are deleted. No container uses it now, and this cannot be undone.",
		"Remove",
		RefreshAsync);
}
