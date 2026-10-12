using System.Globalization;
using Mokaterm.Abstractions.Containers;
using Mokaterm.Modules.Ssh.Docker;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// The host's networks with the containers on each, read from the container list, so a network a stopped container
/// still names counts as in use. Built-in networks and networks in use cannot be removed from here.
/// </summary>
public sealed partial class DockerNetworksPage : DockerManagerPageBase
{
	private IReadOnlyList<DockerNetwork> _networks = [];
	private IReadOnlyList<DockerNetwork> _shown = [];
	private string _query = "";
	private string _name = "";
	private string? _nameError;
	private bool _creating;

	private IReadOnlyList<DockerNetwork> Shown => _shown;

	private List<DockerNetwork> Removable => [.. _networks.Where(network => network.IsRemovable)];

	protected override Task OnInitializedAsync() => RefreshAsync();

	private static string ContainersText(DockerNetwork network) => network.Containers.Count switch
	{
		0 => "",
		1 => "1 container",
		int count => string.Create(CultureInfo.CurrentCulture, $"{count} containers"),
	};

	private static string RemoveTitle(DockerNetwork network) =>
		network.IsBuiltIn ? "Docker's own network cannot be removed"
		: network.InUse ? $"In use by {string.Join(", ", network.Containers)}"
		: $"Remove {network.Name}";

	/// <summary>The name, or the id for a network whose name this app does not pass to docker.</summary>
	private static string Target(DockerNetwork network) =>
		DockerNames.ExistingNameProblem(network.Name, "network") is null ? network.Name : network.Id;

	private Task RefreshAsync() => LoadAsync(async (client, token) =>
	{
		IReadOnlyList<DockerContainer> containers = await client.ListContainersAsync(token);
		_networks = await client.ListNetworksAsync(containers, token);
		ApplyFilter();
	});

	private void ApplyFilter() =>
		_shown = [.. _networks
			.Where(network => network.Matches(_query))
			.OrderBy(network => network.IsBuiltIn)
			.ThenBy(network => network.Name, StringComparer.OrdinalIgnoreCase)];

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

	private async Task CreateAsync()
	{
		string name = _name.Trim();
		if (DockerNames.NewNameProblem(name, "network") is { } problem)
		{
			_nameError = problem;
			return;
		}

		DockerOutput? output = await RunChangeAsync(DockerCommands.CreateNetwork(name), $"Create network {name}", afterwards: RefreshAsync);
		if (output is { Succeeded: true })
		{
			_name = "";
			_creating = false;
		}
	}

	private Task RemoveAsync(DockerNetwork network) => ChangeAsync(
		DockerCommands.RemoveNetworks([Target(network)]),
		$"Remove network {network.Name}",
		$"The network {network.Name} is removed. No container is on it now.",
		"Remove",
		RefreshAsync);

	private Task RemoveUnusedAsync()
	{
		List<DockerNetwork> removable = Removable;
		if (removable.Count == 0)
		{
			return Task.CompletedTask;
		}

		List<string> names = [.. removable.Select(network => network.Name)];
		return ChangeAsync(
			DockerCommands.RemoveNetworks([.. removable.Select(Target)]),
			"Remove unused networks",
			string.Create(CultureInfo.CurrentCulture, $"These {names.Count} networks are removed: {DockerWording.NameList(names)}. No container, running or stopped, is on any of them."),
			"Remove",
			RefreshAsync);
	}
}
