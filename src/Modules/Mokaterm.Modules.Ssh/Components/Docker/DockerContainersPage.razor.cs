using Microsoft.AspNetCore.Components;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.UI.Common.Platform;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// Every container on the host, grouped under its compose project with the rest in a group of their own, one line
/// each: state, name, image, ports. The common actions show on hover, everything else is in the row's menu, and a row
/// opens its panel under the list (beside it on a wide page). The list refreshes every few seconds while it shows.
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

	[Inject]
	private IClipboardService Clipboard { get; set; } = default!;

	private int RunningCount => _containers.Count(container => container.IsAlive);

	protected override async Task OnInitializedAsync()
	{
		await RefreshAsync();
		StartPolling(RefreshInterval, RefreshAsync);
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
			case "logs" or "inspect" or "stats" or "mounts" or "run":
				Select(container, action);
				return;
			case "copy-name":
				await CopyTextAsync(Clipboard, container.Name, "name");
				return;
			case "copy-id":
				await CopyTextAsync(Clipboard, container.Id, "id");
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
