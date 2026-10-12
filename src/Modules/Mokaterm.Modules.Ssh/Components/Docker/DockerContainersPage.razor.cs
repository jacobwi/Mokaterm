using Microsoft.AspNetCore.Components;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.UI.Common.Platform;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// Every container on the host, grouped under its compose project with the rest in a group of their own, one line
/// each: state, name, health, image and whether the session's update check found a newer one, ports. The common
/// actions show on hover, everything else is in the row's menu, and a row opens its panel under the list (beside it on
/// a wide page). "New container" swaps the list for the docker run form. The list refreshes every few seconds while it
/// shows.
/// </summary>
public sealed partial class DockerContainersPage : DockerPageBase
{
	private const string StandaloneKey = "";
	private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);

	private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);
	private IReadOnlyList<DockerContainer> _containers = [];
	private IReadOnlyList<ContainerGroup> _groups = [];
	private string _query = "";
	private string _filter = "all";
	private string? _selected;
	private string _tab = "logs";
	private string? _shellProblem;

	/// <summary>The image the new container form started with, or null while the list shows.</summary>
	private string? _newImage;

	[Inject]
	private IClipboardService Clipboard { get; set; } = default!;

	private int RunningCount => _containers.Count(container => container.IsAlive);

	private IReadOnlyCollection<string> TakenNames => [.. _containers.Select(container => container.Name)];

	protected override async Task OnInitializedAsync()
	{
		if (Navigation?.TakeNewContainerImage() is { } image)
		{
			_newImage = image;
		}

		await RefreshAsync();
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

	/// <summary>Running containers first, then by name, whatever docker's own order was.</summary>
	private static int Rank(DockerContainer container) => container.State switch
	{
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

	private void ApplyFilter()
	{
		IEnumerable<DockerContainer> shown = _containers
			.Where(container => _filter switch
			{
				"running" => container.IsAlive,
				"stopped" => !container.IsAlive,
				_ => true,
			})
			.Where(container => container.Matches(_query));

		_groups = [.. shown
			.GroupBy(container => container.ComposeProject ?? StandaloneKey, StringComparer.Ordinal)
			.OrderBy(group => group.Key.Length == 0)
			.ThenBy(group => group.Key, StringComparer.Ordinal)
			.Select(group => new ContainerGroup(
				group.Key,
				group.Key.Length == 0 ? "Standalone" : group.Key,
				[.. group.OrderBy(Rank).ThenBy(container => container.Name, StringComparer.OrdinalIgnoreCase)]))];
	}

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

	private void ToggleGroup(string key)
	{
		if (!_collapsed.Remove(key))
		{
			_collapsed.Add(key);
		}
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

	private void CloseNewContainer() => _newImage = null;

	/// <summary>Back to the list, with the new container open in its panel.</summary>
	private async Task OnCreatedAsync(string id)
	{
		_newImage = null;
		await RefreshAsync();
		if (_containers.FirstOrDefault(container => container.Id.StartsWith(id, StringComparison.Ordinal) || container.Name == id) is { } created)
		{
			_collapsed.Remove(created.ComposeProject ?? StandaloneKey);
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
		if (ComposeUpdate(container) is { } plan)
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

	/// <summary>The update of the compose service <paramref name="container"/> belongs to, when its project's files can be found.</summary>
	private ComposeActionPlan? ComposeUpdate(DockerContainer container)
	{
		if (container.ComposeService is not { } service
			|| Feature?.Compose is not { } tool
			|| ComposeProject.Parse(null, _containers).FirstOrDefault(project => project.Name == container.ComposeProject) is not { } project)
		{
			return null;
		}

		ComposeTarget target = ComposeTarget.For(tool, project);
		IReadOnlyList<string> services = [.. project.Containers.Select(member => member.ComposeService).OfType<string>().Distinct(StringComparer.Ordinal)];
		return target.HasFiles ? ComposeActions.Plan("update", target, services, service) : null;
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

	/// <summary>A compose project's containers, or the standalone ones under an empty key.</summary>
	private sealed record ContainerGroup(string Key, string Title, IReadOnlyList<DockerContainer> Containers)
	{
		public int Running => Containers.Count(container => container.IsRunning);
	}
}
