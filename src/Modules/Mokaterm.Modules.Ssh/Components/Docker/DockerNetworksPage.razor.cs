using System.Globalization;
using Microsoft.AspNetCore.Components;
using Moka.Red.ContextMenu;
using Moka.Red.Icons;
using Mokaterm.Abstractions.Containers;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.UI.Common.Platform;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// The host's networks in one table with the containers on each, read from the container list, so a network a stopped
/// container still names counts as in use. Built-in networks and networks in use cannot be removed from here.
/// </summary>
public sealed partial class DockerNetworksPage : DockerManagerPageBase
{
	private IReadOnlyList<DockerNetwork> _networks = [];
	private IReadOnlyList<DockerNetwork> _shown = [];
	private string _query = "";
	private string _name = "";
	private string? _nameError;
	private bool _creating;

	[Inject]
	private IClipboardService Clipboard { get; set; } = default!;

	private List<DockerNetwork> Removable => [.. _networks.Where(network => network.IsRemovable)];

	private string? Summary => IsFirstLoad
		? null
		: string.Create(CultureInfo.CurrentCulture, $"{_networks.Count} networks, {_networks.Count(network => network.InUse)} in use, {_networks.Count(network => network.IsBuiltIn)} built in");

	protected override Task OnInitializedAsync() => RefreshAsync();

	private static string ContainersText(DockerNetwork network) => string.Join(", ", network.Containers);

	private static string KindText(DockerNetwork network) => network.IsBuiltIn ? "built in" : network.Internal ? "internal" : "";

	private static int ByName(DockerNetwork left, DockerNetwork right) => StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);

	/// <summary>The row's menu, from its button or a right-click.</summary>
	private List<MokaContextMenuItem> MenuFor(DockerNetwork network) =>
	[
		new MokaContextMenuItem { Text = "Copy name", Icon = MokaIcons.Content.Copy, OnClick = () => CopyTextAsync(Clipboard, network.Name, "name") },
		new MokaContextMenuItem { Text = $"Copy id {network.ShortId}", Icon = MokaIcons.Content.Copy, OnClick = () => CopyTextAsync(Clipboard, network.Id, "id") },
		new MokaContextMenuItem { Text = RemoveTitle(network), Icon = MokaIcons.Action.Delete, Disabled = IsBusy || !network.IsRemovable, DividerBefore = true, OnClick = () => RemoveAsync(network) },
	];

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

	/// <summary>The picked networks that can go, in one <c>docker network rm</c>. One alone goes the way its own row's Remove does.</summary>
	private Task RemoveManyAsync(IReadOnlyList<DockerNetwork> picked)
	{
		List<DockerNetwork> removable = [.. picked.Where(network => network.IsRemovable)];
		if (removable.Count == 0)
		{
			return Task.CompletedTask;
		}

		if (removable is [var only] && picked.Count == 1)
		{
			return RemoveAsync(only);
		}

		int kept = picked.Count - removable.Count;
		string keeping = kept switch
		{
			0 => "",
			1 => " The other one picked is Docker's own or in use, and stays.",
			_ => string.Create(CultureInfo.CurrentCulture, $" The other {kept} picked are Docker's own or in use, and stay."),
		};
		List<string> names = [.. removable.Select(network => network.Name)];
		string message = removable is [var one]
			? $"The network {one.Name} is removed. No container is on it now."
			: string.Create(CultureInfo.CurrentCulture, $"These {names.Count} networks are removed: {DockerWording.NameList(names)}. No container is on any of them now.");
		return ChangeAsync(
			DockerCommands.RemoveNetworks([.. removable.Select(Target)]),
			removable is [var single] ? $"Remove network {single.Name}" : string.Create(CultureInfo.CurrentCulture, $"Remove {names.Count} networks"),
			message + keeping,
			"Remove",
			RefreshAsync);
	}

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
