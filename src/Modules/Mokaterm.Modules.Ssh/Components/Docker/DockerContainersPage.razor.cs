using System.Globalization;
using Microsoft.AspNetCore.Components;
using Moka.Red.ContextMenu;
using Mokaterm.Abstractions.Containers;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.UI.Common.Platform;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// Every container on the host in one table, grouped under its compose project with the rest under Standalone: state,
/// name, health, image, whether it is in a restart loop and whether the session's update check found a newer one, and
/// ports, with the status in the dot's hint. The common actions show on a row that is pointed at or has the focus,
/// everything else is in the row's menu (its button or a right-click), and a row opens its panel beside the list (under
/// it on a narrow page). "New container" swaps the list for the docker run form. The list refreshes every few seconds
/// while it shows.
/// </summary>
public sealed partial class DockerContainersPage : DockerManagerPageBase
{
	private const string StandaloneKey = "";
	private const string LoopsFilter = "loops";
	private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);

	private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);
	private IReadOnlyList<DockerContainer> _containers = [];
	private IReadOnlyList<DockerContainer> _shown = [];

	// The table starts from this set and goes back to it whenever it is handed a new one, so it is replaced on every fold
	// to stay what the user folded, and once more to open the group of a container asked for.
	private IReadOnlyCollection<string>? _collapsedGroups;
	private string _query = "";
	private string _filter = "all";
	private string? _selected;
	private string _tab = "logs";
	private string? _shellProblem;

	/// <summary>The image the new container form started with, or null while the list shows.</summary>
	private string? _newImage;

	[Inject]
	private IClipboardService Clipboard { get; set; } = default!;

	private bool ShowsList => _newImage is null;

	private int RunningCount => _containers.Count(container => container.IsAlive);

	private int LoopingCount => _containers.Count(IsLooping);

	private int ProjectCount => _containers.Select(container => container.ComposeProject).OfType<string>().Distinct(StringComparer.Ordinal).Count();

	private IReadOnlyCollection<string> TakenNames => [.. _containers.Select(container => container.Name)];

	private DockerContainer? SelectedContainer => _selected is { } id ? _containers.FirstOrDefault(container => container.Id == id) : null;

	private string? Summary => IsFirstLoad
		? null
		: string.Create(
			CultureInfo.CurrentCulture,
			$"{_containers.Count} containers, {RunningCount} up, {_containers.Count - RunningCount} down{(LoopingCount > 0 ? $", {LoopingCount} looping" : "")} · {ProjectCount} compose {(ProjectCount == 1 ? "project" : "projects")}");

	private EventCallback<string> SearchCallback => ShowsList ? EventCallback.Factory.Create<string>(this, OnQueryChanged) : default;

	private EventCallback RefreshCallback => ShowsList ? EventCallback.Factory.Create(this, () => RefreshByHandAsync(RefreshAsync)) : default;

	protected override async Task OnInitializedAsync()
	{
		if (Navigation?.TakeNewContainerImage() is { } image)
		{
			_newImage = image;
		}

		if (Navigation?.TakeRestartLoops() == true)
		{
			_filter = LoopsFilter;
		}

		(string Id, string Tab)? asked = Navigation?.TakeContainer();
		await RefreshAsync();
		if (asked is { } show && _containers.FirstOrDefault(container => container.Id == show.Id) is { } shown)
		{
			OpenGroup(shown);
			Select(shown, show.Tab);
		}

		FollowRestartLoops(ApplyFilter);
		StartPolling(RefreshInterval, () => _newImage is null ? RefreshAsync() : Task.CompletedTask);
	}

	/// <summary>Within a project's group the project's prefix says nothing, so <c>shop-web-1</c> shows as <c>web-1</c>.</summary>
	private static string ShortName(DockerContainer container) =>
		container.ComposeProject is { } project && container.Name.StartsWith(project + "-", StringComparison.Ordinal) && container.Name.Length > project.Length + 1
			? container.Name[(project.Length + 1)..]
			: container.Name;

	/// <summary>The published ports, or the exposed ones when nothing is published.</summary>
	private static string Ports(DockerContainer container)
	{
		List<string> ports = [.. container.Ports.Where(port => port.IsPublished).Select(port => port.Display)];
		return string.Join(", ", ports.Count > 0 ? ports : container.Ports.Select(port => port.Display));
	}

	/// <summary>
	/// Running containers first, then by name, whatever docker's own order was. One in a restart loop keeps the place of a
	/// restarting one while it runs, so its row does not jump at every read.
	/// </summary>
	private int Rank(DockerContainer container) => container.State switch
	{
		"running" when IsLooping(container) => 1,
		"running" => 0,
		"restarting" or "paused" => 1,
		"created" => 3,
		_ => 2,
	};

	private Task RefreshAsync() => LoadAsync(async (client, token) =>
	{
		_containers = await client.ListContainersAsync(token);
		if (_selected is { } selected && !_containers.Any(container => container.Id == selected))
		{
			_selected = null;
		}

		ApplyFilter();
	});

	/// <summary>
	/// The rows in the order the table groups them: projects by name with Standalone last, since the table builds its
	/// groups in the order their first rows come, and within a project the running containers first.
	/// </summary>
	private void ApplyFilter() =>
		_shown = [.. _containers
			.Where(container => _filter switch
			{
				"running" => container.IsAlive,
				"stopped" => !container.IsAlive,
				LoopsFilter => IsLooping(container),
				_ => true,
			})
			.Where(container => container.Matches(_query))
			.OrderBy(container => GroupKey(container).Length == 0)
			.ThenBy(GroupKey, StringComparer.Ordinal)
			.ThenBy(Rank)
			.ThenBy(container => container.Name, StringComparer.OrdinalIgnoreCase)];

	private static string GroupKey(DockerContainer container) => container.ComposeProject ?? StandaloneKey;

	private static int ByName(DockerContainer left, DockerContainer right) => StringComparer.OrdinalIgnoreCase.Compare(ShortName(left), ShortName(right));

	private static int ByImage(DockerContainer left, DockerContainer right) => StringComparer.OrdinalIgnoreCase.Compare(left.Image, right.Image);

	private static int ByPorts(DockerContainer left, DockerContainer right) => StringComparer.Ordinal.Compare(Ports(left), Ports(right));

	private static int ByHealth(DockerContainer left, DockerContainer right) => HealthOrder(left.Health).CompareTo(HealthOrder(right.Health));

	private static int HealthOrder(DockerHealth health) => health switch
	{
		DockerHealth.Unhealthy => 0,
		DockerHealth.Starting => 1,
		DockerHealth.Healthy => 2,
		_ => 3,
	};

	private bool HasUpdate(DockerContainer container) => Feature?.ImageUpdates.Find(container.Image) is { HasUpdate: true };

	private List<MokaContextMenuItem> MenuFor(DockerContainer container) =>
		DockerContainerMenu.Items(container.State, CanOpenShellTab, HasUpdate(container), IsBusy, action => OnMenuAsync(container, action));

	private void OnQueryChanged(string value)
	{
		_query = value ?? "";
		ApplyFilter();
	}

	private void OnFilterChanged(string? value)
	{
		_filter = value ?? "all";
		ApplyFilter();
	}

	private void OnGroupToggled((string Key, bool Collapsed) change)
	{
		if (change.Collapsed)
		{
			_collapsed.Add(change.Key);
		}
		else
		{
			_collapsed.Remove(change.Key);
		}

		_collapsedGroups = [.. _collapsed];
	}

	private void OpenGroup(DockerContainer container)
	{
		if (_collapsed.Remove(GroupKey(container)))
		{
			_collapsedGroups = [.. _collapsed];
		}
	}

	private bool IsLooping(DockerContainer container) => LoopOf(container.Id, container.Name) is not null;

	private string FirstTab(DockerContainer container) => IsLooping(container) ? DockerContainerPanel.RestartsTab : "logs";

	/// <summary>A click on a row, or Enter on one of its cells: opens its panel, or closes it when it is already open.</summary>
	private void OnRowClick(DockerContainer container)
	{
		if (container.Id == _selected)
		{
			ClosePanel();
			return;
		}

		Select(container, FirstTab(container));
	}

	private void Select(DockerContainer container, string tab)
	{
		_selected = container.Id;
		_tab = tab;
		_shellProblem = null;
	}

	private void OnTabChanged(string tab) => _tab = tab;

	private void ClosePanel() => _selected = null;

	private void NewContainer(string image)
	{
		_newImage = image;
		_shellProblem = null;
	}

	private void CloseNewContainer()
	{
		_newImage = null;
		_collapsedGroups = [.. _collapsed];
	}

	/// <summary>Back to the list, with the new container open in its panel.</summary>
	private async Task OnCreatedAsync(string id)
	{
		CloseNewContainer();
		await RefreshAsync();
		if (_containers.FirstOrDefault(container => container.Id.StartsWith(id, StringComparison.Ordinal) || container.Name == id) is { } created)
		{
			OpenGroup(created);
			Select(created, "logs");
		}
	}

	/// <summary>
	/// A compose container's service is pulled and brought up again through the project's own update; any other
	/// container only gets the newer image, since making it again takes the docker run it was started with.
	/// </summary>
	private async Task UpdateAsync(DockerContainer container)
	{
		bool updated;
		if (ComposePlan("update", container, _containers) is { } plan)
		{
			updated = await RunChangesAsync(plan.Calls, plan.Title, plan.Confirm, plan.ConfirmText, RefreshAsync) is { Count: > 0 } outputs
				&& outputs.All(output => output.Succeeded);
		}
		else
		{
			DockerOutput? output = await RunChangeAsync(
				DockerCommands.Pull(container.Image),
				$"Pull the newer {container.Image}",
				$"The newer {container.Image} is pulled, and {container.Name} keeps running the old one. Making {container.Name} again from the new image takes the docker run it was first started with, which this app does not work out from the container: remove it and run it again the same way.",
				"Pull",
				RefreshAsync,
				destructive: false);
			updated = output is { Succeeded: true };
		}

		if (updated)
		{
			Feature?.ImageUpdates.Forget([container.Image]);
		}
	}

	private async Task StartAsync(DockerContainer container) => await RunPlanAsync(DockerContainerActions.Plan("start", container.Name, container.IsAlive)!, RefreshAsync);

	private async Task RestartAsync(DockerContainer container) => await RunPlanAsync(DockerContainerActions.Plan("restart", container.Name, container.IsAlive)!, RefreshAsync);

	private async Task ShellAsync(DockerContainer container) => _shellProblem = await OpenShellAsync(container.Name);

	private async Task OnMenuAsync(DockerContainer container, string action)
	{
		switch (action)
		{
			case "shell":
				await ShellAsync(container);
				return;
			case "files":
				await BrowseFilesAsync(container.Name);
				return;
			case "logs" or "inspect" or "stats" or "mounts" or "run":
				Select(container, action);
				return;
			case "copy-name":
				await CopyTextAsync(Clipboard, container.Name, "name");
				return;
			case "copy-id":
				await CopyTextAsync(Clipboard, container.Id, "id");
				return;
			case "update":
				await UpdateAsync(container);
				return;
		}

		if (DockerContainerActions.Plan(action, container.Name, container.IsAlive) is { } plan)
		{
			await RunPlanAsync(plan, RefreshAsync);
		}
	}
}
