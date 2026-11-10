using System.Globalization;
using Microsoft.AspNetCore.Components;
using Moka.Red.ContextMenu;
using Moka.Red.Core.Enums;
using Moka.Red.Icons;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.UI.Common.Icons;
using Mokaterm.UI.Common.Platform;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// The host's compose projects in one table: <c>compose ls -a</c> where the compose has it, completed from the container
/// labels, which are the whole list on a host with the Python compose. A row opens the project in place of the list;
/// "Find compose files" searches the usual folders for files that run as no project yet and lists them beside the
/// table, where each can be brought up.
/// </summary>
public sealed partial class DockerComposePage : DockerManagerPageBase
{
	private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(10);

	private IReadOnlyList<ComposeProject> _projects = [];
	private IReadOnlyList<ComposeProject> _shown = [];
	private IReadOnlyList<string>? _found;
	private string _query = "";
	private string? _selected;
	private string? _findProblem;
	private bool _finding;

	[Inject]
	private IClipboardService Clipboard { get; set; } = default!;

	private bool ShowsList => _selected is null;

	private string? Summary => IsFirstLoad || !ShowsList
		? null
		: string.Create(
			CultureInfo.CurrentCulture,
			$"{_projects.Count} {(_projects.Count == 1 ? "project" : "projects")}, {_projects.Count(project => project.RunningCount > 0)} running · {Feature?.Compose?.Display ?? "no compose found"}");

	private EventCallback<string> SearchCallback => ShowsList ? EventCallback.Factory.Create<string>(this, OnQueryChanged) : default;

	private EventCallback RefreshCallback => ShowsList ? EventCallback.Factory.Create(this, () => RefreshByHandAsync(RefreshAsync)) : default;

	private string? ToolNote => Feature?.Compose switch
	{
		null => "No compose was found on this host (or not the one picked in the overview): projects come from their containers' labels and can be looked at, not changed.",
		{ SupportsList: false } => "This compose has no ls, so projects come from their containers' labels: a project with no container left does not show.",
		_ => null,
	};

	private static string SearchedText => "your home, " + string.Join(", ", DockerHostScripts.SearchRoots);

	private static string FindTitle => $"Looks under {SearchedText} for compose.yaml, compose.yml, docker-compose.yaml and docker-compose.yml";

	protected override async Task OnInitializedAsync()
	{
		await RefreshAsync();
		await ReadDriftAsync();
		StartPolling(RefreshInterval, async () =>
		{
			if (_selected is null)
			{
				await RefreshAsync();
				await ReadDriftAsync();
			}
		});
	}

	private ComposeDrift? DriftOf(ComposeProject project) =>
		Feature is { } feature ? ComposeDrift.Compare(feature.ComposeDrift.Find(project.Name), project.Containers, feature.Compose) : null;

	// At most two projects per refresh, and only those whose last answer is older than the list takes, so a host with
	// many projects is read a few at a time rather than all at once.
	private async Task ReadDriftAsync()
	{
		if (Feature is not { Compose: { } tool } feature || Client is not { } client)
		{
			return;
		}

		int reads = 0;
		foreach (ComposeProject project in _projects)
		{
			ComposeTarget target = ComposeTarget.For(tool, project);
			if (reads == 2 || IsDisposed || !target.HasFiles || !feature.ComposeDrift.IsStale(project.Name, tool, DockerComposeDrift.ListAge))
			{
				continue;
			}

			reads++;
			try
			{
				await feature.ComposeDrift.ReadAsync(client, target, Lifetime);
			}
			catch (OperationCanceledException)
			{
				return;
			}
		}

		if (reads > 0 && !IsDisposed)
		{
			StateHasChanged();
		}
	}

	private static MokaColor ProjectColor(ComposeProject project) => project.Health switch
	{
		ComposeProjectHealth.Running => MokaColor.Success,
		ComposeProjectHealth.Partial => MokaColor.Warning,
		ComposeProjectHealth.Unhealthy => MokaColor.Error,
		_ => MokaColor.Surface,
	};

	private static int ByName(ComposeProject left, ComposeProject right) => StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);

	/// <summary>How many of a project's containers are in each state, the ones that run first.</summary>
	private static List<(string State, int Count)> StateCounts(ComposeProject project) =>
		[.. project.Containers
			.GroupBy(container => container.State, StringComparer.Ordinal)
			.OrderBy(group => group.Key switch { "running" => 0, "restarting" or "paused" => 1, "created" => 2, "exited" => 3, _ => 4 })
			.ThenBy(group => group.Key, StringComparer.Ordinal)
			.Select(group => (group.Key, group.Count()))];

	private static int ByRunning(ComposeProject left, ComposeProject right) => left.RunningCount.CompareTo(right.RunningCount);

	/// <summary>The row's menu, from its button or a right-click.</summary>
	private List<MokaContextMenuItem> MenuFor(ComposeProject project)
	{
		List<MokaContextMenuItem> items = [new MokaContextMenuItem { Text = "Open", Icon = DockerIcons.Compose, OnClickSync = () => Open(project) }];
		if (Feature?.Compose is not null)
		{
			items.Add(new MokaContextMenuItem { Text = "Restart", Icon = MokatermIcons.Reconnect, Disabled = IsBusy, OnClick = () => RestartAsync(project) });
		}

		if (project.WorkingDirectory is { } folder)
		{
			items.Add(new MokaContextMenuItem { Text = "Copy the folder", Icon = MokaIcons.Content.Copy, DividerBefore = true, OnClick = () => CopyTextAsync(Clipboard, folder, "folder") });
		}

		return items;
	}

	private Task RefreshAsync() => LoadAsync(async (client, token) =>
	{
		IReadOnlyList<DockerContainer> containers = await client.ListContainersAsync(token);
		_projects = await client.ListComposeProjectsAsync(Feature?.Compose, containers, token);
		ApplyFilter();
		if (_found is not null)
		{
			_found = WithoutProjects(_found);
		}
	});

	private void ApplyFilter() => _shown = [.. _projects.Where(project => project.Matches(_query))];

	private void OnQueryChanged(string value)
	{
		_query = value ?? "";
		ApplyFilter();
	}

	private void Open(ComposeProject project) => _selected = project.Name;

	private Task CloseProjectAsync()
	{
		_selected = null;
		return RefreshAsync();
	}

	private async Task FindFromProjectAsync()
	{
		_selected = null;
		await FindAsync();
	}

	private void CloseFound()
	{
		_found = null;
		_findProblem = null;
	}

	/// <summary>Files under the usual roots that no listed project already runs from.</summary>
	private async Task FindAsync()
	{
		if (Client is not { } client || _finding)
		{
			return;
		}

		_finding = true;
		_findProblem = null;
		StateHasChanged();
		try
		{
			_found = WithoutProjects(await client.FindComposeFilesAsync(Lifetime));
		}
		catch (Exception ex) when (Describe(ex) is { } problem)
		{
			_found = [];
			_findProblem = problem;
		}
		catch (OperationCanceledException)
		{
			return;
		}
		finally
		{
			_finding = false;
		}

		StateHasChanged();
	}

	/// <summary>A file a project names, or the default file in a project's folder, already runs.</summary>
	private List<string> WithoutProjects(IReadOnlyList<string> files)
	{
		HashSet<string> running = new(_projects.SelectMany(project => project.ConfigFiles), StringComparer.Ordinal);
		HashSet<string> folders = new(_projects.Select(project => project.WorkingDirectory).OfType<string>(), StringComparer.Ordinal);
		return [.. files.Where(file => !running.Contains(file) && !folders.Contains(DockerHostPaths.DirectoryOf(file)))];
	}

	private Task RestartAsync(ComposeProject project) => Feature?.Compose is { } tool
		? RunChangesAsync([ComposeCommands.Restart(ComposeTarget.For(tool, project))], $"Restart {project.Name}", afterwards: RefreshAsync)
		: Task.CompletedTask;

	/// <summary>
	/// <c>cd -- DIR &amp;&amp; docker compose -f FILE up --detach</c>, with no <c>-p</c>: compose names the project itself,
	/// from the file's <c>name:</c> or its folder, as it would at a prompt there. Asked first, with the command shown.
	/// </summary>
	private Task UpAsync(string file)
	{
		if (Feature?.Compose is not { } tool)
		{
			return Task.CompletedTask;
		}

		ComposeTarget target = ComposeTarget.ForFile(tool, file);
		string folder = DockerHostPaths.DirectoryOf(file);
		return RunChangesAsync(
			[ComposeCommands.Up(target)],
			$"Bring up {folder}",
			$"Compose starts every service {DockerHostPaths.NameOf(file)} defines, pulling and building what it needs, under the project name the file or its folder ({DockerHostPaths.ProjectNameFor(folder)}) gives it.",
			"Up",
			RefreshAsync,
			destructive: false);
	}
}
