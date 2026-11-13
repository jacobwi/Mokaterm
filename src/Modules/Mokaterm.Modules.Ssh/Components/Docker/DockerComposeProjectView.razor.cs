using System.Globalization;
using Microsoft.AspNetCore.Components;
using Moka.Red.ContextMenu;
using Moka.Red.Core.Enums;
using Moka.Red.Core.Icons;
using Moka.Red.Icons;
using Mokaterm.Abstractions.Containers;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.UI.Common.Formatting;
using Mokaterm.UI.Common.Icons;
using Mokaterm.UI.Common.Platform;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// One compose project in one place: a header with its state, its folder and its compose; the actions used most as
/// labelled buttons and the rest in a menu (all in the menu when the page is narrow); a table with a row per service and
/// its own actions, where a scaled service opens to its containers; and its logs, files, env files and configuration as
/// tabs. Every action shows what it touches while it runs, keeps the tail of its output from the last run, and refreshes
/// the project after.
/// </summary>
public sealed partial class DockerComposeProjectView : DockerManagerPageBase
{
	private static readonly IReadOnlyList<(string Id, string Text)> Tabs =
	[
		("services", "Services"),
		("logs", "Logs"),
		("files", "Files"),
		("env", "Env"),
		("config", "Config"),
	];

	private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);

	/// <summary>The tab a build's output shows in, there while the project has a build running or not yet dismissed.</summary>
	private const string BuildTab = "build";

	private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);
	private ComposeProject? _project;
	private ComposeTarget? _target;
	private IReadOnlyList<ComposeService> _services = [];
	private IReadOnlyList<ServiceLine> _lines = [];
	private IReadOnlyList<string> _defined = [];
	private ComposeProjectContext? _context;
	private int _version;
	private string _tab = "services";
	private string? _logService;
	private bool _build;
	private bool _pullFirst;
	private bool _checkingDrift;

	[Parameter, EditorRequired]
	public string ProjectName { get; set; } = "";

	[Inject]
	private IClipboardService Clipboard { get; set; } = default!;

	[Parameter]
	public EventCallback OnBack { get; set; }

	/// <summary>Asks the page to search the disk for compose files.</summary>
	[Parameter]
	public EventCallback OnFind { get; set; }

	private ComposeTool? Tool => Feature?.Compose;

	private IReadOnlyList<ServiceLine> Lines => _lines;

	private string LiveOwner => DockerLivePlan.ComposeOwner(ProjectName);

	/// <summary>The project's tabs, with the build's output among them while there is a build to show.</summary>
	private IReadOnlyList<(string Id, string Text)> ShownTabs =>
		LiveRuns?.Current is { } run && run.Owner == LiveOwner ? [.. Tabs, (BuildTab, run.IsRunning ? "Build (running)" : "Build")] : Tabs;

	private ComposeProjectHealth Health => ComposeService.HealthOf(_services);

	/// <summary>The images the services run, for the update check.</summary>
	private IReadOnlyList<string> ServiceImages => [.. _services.Select(service => service.Image).Where(image => image.Length > 0).Distinct(StringComparer.Ordinal)];

	private string ServiceCount => string.Create(
		CultureInfo.InvariantCulture,
		$"{_services.Count(service => service.State == ComposeServiceState.Running)}/{_services.Count}");

	private string NoTargetText => Tool is null
		? "No compose was found on this host (or not the one picked in the overview), so the project can be looked at but not changed."
		: "Reading the project.";

	// Compared with the containers' labels at every draw; the files' side is the session's, read at most once a minute.
	private ComposeDrift? Drift => _project is { } project && Feature is { } feature
		? ComposeDrift.Compare(feature.ComposeDrift.Find(project.Name), project.Containers, Tool)
		: null;

	protected override async Task OnInitializedAsync()
	{
		FollowRestartLoops();
		await RefreshAsync();
		await ReadDriftAsync(force: false);
		StartPolling(RefreshInterval, async () =>
		{
			await RefreshAsync();
			await ReadDriftAsync(force: false);
		});
	}

	private string DriftCheckLabel(ComposeDrift drift) => string.Create(
		CultureInfo.CurrentCulture,
		$"Compare with the files again (last read {DisplayFormat.Relative(drift.ReadAt, Time.GetUtcNow())})");

	private Task CheckDriftAsync() => ReadDriftAsync(force: true);

	private async Task ReadDriftAsync(bool force)
	{
		if (_target is not { HasFiles: true } target || Feature is not { } feature || Client is not { } client
			|| (!force && !feature.ComposeDrift.IsStale(ProjectName, target.Tool, DockerComposeDrift.ProjectAge)))
		{
			return;
		}

		_checkingDrift = true;
		try
		{
			await feature.ComposeDrift.ReadAsync(client, target, Lifetime);
		}
		catch (OperationCanceledException)
		{
			return;
		}
		finally
		{
			_checkingDrift = false;
		}

		if (!IsDisposed)
		{
			StateHasChanged();
		}
	}

	private static MokaColor HealthColor(ComposeProjectHealth health) => health switch
	{
		ComposeProjectHealth.Running => MokaColor.Success,
		ComposeProjectHealth.Partial => MokaColor.Warning,
		ComposeProjectHealth.Unhealthy => MokaColor.Error,
		_ => MokaColor.Surface,
	};

	private static string HealthLabel(ComposeProjectHealth health) => health switch
	{
		ComposeProjectHealth.Running => "RUNNING",
		ComposeProjectHealth.Partial => "PARTLY RUNNING",
		ComposeProjectHealth.Unhealthy => "UNHEALTHY",
		ComposeProjectHealth.Stopped => "STOPPED",
		_ => "EMPTY",
	};

	private static MokaColor ServiceColor(ComposeService service) => service.State switch
	{
		ComposeServiceState.Running when service.Health == DockerHealth.Starting => MokaColor.Warning,
		ComposeServiceState.Running => MokaColor.Success,
		ComposeServiceState.Partial => MokaColor.Warning,
		ComposeServiceState.Failed or ComposeServiceState.Unhealthy => MokaColor.Error,
		_ => MokaColor.Surface,
	};

	private static string ServiceTitle(ComposeService service) => service.State switch
	{
		ComposeServiceState.NoContainer => "Defined in the files, with no container yet",
		ComposeServiceState.Failed => $"Failed: {service.Problem}",
		ComposeServiceState.Unhealthy => "A container's health check fails",
		ComposeServiceState.Partial => "Some containers run and some do not",
		ComposeServiceState.Stopped => "Stopped",
		_ => service.IsScaled ? "Running; opens to its containers" : "Running",
	};

	/// <summary>
	/// The project from the container list and <c>compose ls</c>, its containers from <c>compose ps</c> where the compose
	/// prints JSON (from the labels otherwise), and the services its files define, read once.
	/// </summary>
	private Task RefreshAsync() => LoadAsync(async (client, token) =>
	{
		IReadOnlyList<DockerContainer> containers = await client.ListContainersAsync(token);
		IReadOnlyList<ComposeProject> projects = await client.ListComposeProjectsAsync(Tool, containers, token);
		_project = projects.FirstOrDefault(project => project.Name == ProjectName);
		if (_project is not { } project)
		{
			_target = null;
			_services = [];
			_lines = [];
			_context = null;
			return;
		}

		_target = Tool is { } tool ? ComposeTarget.For(tool, project) : null;
		IReadOnlyList<ComposeContainer> members = _target is { Tool.SupportsJson: true } target
			? await client.ComposeContainersAsync(target, token)
			: [.. project.Containers.Select(ComposeContainer.FromContainer)];

		if (_defined.Count == 0 && _target is { HasFiles: true } withFiles)
		{
			try
			{
				_defined = await client.ComposeServicesAsync(withFiles, token);
			}
			catch (DockerCommandException)
			{
				// The files moved or do not parse: the containers' own services are what is known then.
				_defined = [];
			}
		}

		_services = ComposeService.Group(members, _defined);
		_context = new ComposeProjectContext(project, _target, _services, ++_version, RunAsync);
		BuildLines();
	});

	/// <summary>Every service, with a scaled one's containers under it while it is open.</summary>
	private void BuildLines()
	{
		List<ServiceLine> lines = [];
		foreach (ComposeService service in _services)
		{
			lines.Add(new ServiceLine(service, null));
			if (service.IsScaled && _expanded.Contains(service.Name))
			{
				lines.AddRange(service.Containers.Select(container => new ServiceLine(service, container)));
			}
		}

		_lines = lines;
	}

	private static string? LineClass(ServiceLine line) =>
		line.Container is not null ? "mt-dk-row--nested"
		: line.Service.State is ComposeServiceState.Stopped or ComposeServiceState.NoContainer ? "mt-dk-row--down"
		: null;

	/// <summary>A row's menu, from its button or a right-click: a service's actions, or a scaled one's container's.</summary>
	private List<MokaContextMenuItem> MenuFor(ServiceLine line) =>
		line.Container is { } container ? ContainerMenu(container) : MenuFor(line.Service);

	private List<MokaContextMenuItem> MenuFor(ComposeService service)
	{
		if (_target is not { } target)
		{
			return [];
		}

		bool files = target.HasFiles;
		List<MokaContextMenuItem> items =
		[
			Action("Restart", MokatermIcons.Reconnect, "restart", service, IsBusy),
			Action("Stop", DockerIcons.Stop, "stop", service, IsBusy),
			Action("Start", MokatermIcons.Play, "start", service, IsBusy),
			Action("Recreate this service", MokatermIcons.Reconnect, "recreate", service, IsBusy || !files),
			Action("Pull its image", MokaIcons.Action.Download, "pull", service, IsBusy || !files),
			Action("Build its image", DockerIcons.Build, "build", service, IsBusy || !files || IsWorking),
			Action("Update this service (pull, then up)", DockerIcons.Update, "update", service, IsBusy || !files),
		];
		bool first = true;
		if (ServiceLoop(service) is { } loop && Navigation is { } navigation)
		{
			items.Add(new MokaContextMenuItem
			{
				Text = "Why it keeps restarting",
				Icon = DockerIcons.Container,
				DividerBefore = true,
				OnClickSync = () => navigation.ShowContainer(loop.Id, DockerContainerPanel.RestartsTab),
			});
			first = false;
		}

		items.Add(new MokaContextMenuItem { Text = "Logs", Icon = MokaIcons.File.FileText, DividerBefore = first, OnClickSync = () => ShowLogs(service.Name) });
		items.Add(new MokaContextMenuItem
		{
			Text = "Browse files",
			Icon = MokatermIcons.FolderOpen,
			Disabled = RunningContainer(service) is null,
			OnClick = () => RunningContainer(service) is { } browsed ? BrowseFilesAsync(browsed.Name) : Task.CompletedTask,
		});
		return items;
	}

	private MokaContextMenuItem Action(string text, MokaIconDefinition icon, string action, ComposeService service, bool disabled) => new()
	{
		Text = text,
		Icon = icon,
		Disabled = disabled,
		OnClick = () => RunAsync(action, service.Name),
	};

	private List<MokaContextMenuItem> ContainerMenu(ComposeContainer container) =>
	[
		new MokaContextMenuItem
		{
			Text = "Open a shell in a new tab",
			Icon = MokatermIcons.Terminal,
			Disabled = IsBusy || !container.IsRunning || !CanOpenShellTab,
			OnClick = () => ShellAsync(container.Name),
		},
		new MokaContextMenuItem { Text = "Browse files", Icon = MokatermIcons.FolderOpen, Disabled = !container.IsRunning, OnClick = () => BrowseFilesAsync(container.Name) },
		new MokaContextMenuItem { Text = "Copy name", Icon = MokaIcons.Content.Copy, DividerBefore = true, CssClass = CopyClick.Class, OnClick = () => CopyTextAsync(Clipboard, container.Name, "name") },
	];

	/// <summary>The ports of a service's running containers: a scaled service may publish one per replica.</summary>
	private static IReadOnlyList<DockerPort> RunningPorts(ComposeService service) =>
		[.. service.Containers.Where(container => container.IsRunning).SelectMany(container => container.Ports)];

	private void Show(string tab) => _tab = tab;

	private void ShowLogs(string service)
	{
		_logService = service;
		_tab = "logs";
	}

	/// <summary>A click on a scaled service, or Enter on one of its cells, opens it to its containers or folds it again.</summary>
	private void ToggleService(ServiceLine line)
	{
		if (line.Container is not null || !line.Service.IsScaled)
		{
			return;
		}

		if (!_expanded.Remove(line.Service.Name))
		{
			_expanded.Add(line.Service.Name);
		}

		BuildLines();
	}

	private void ToggleBuild() => _build = !_build;

	private void TogglePullFirst() => _pullFirst = !_pullFirst;

	private DockerRestartRecord? ServiceLoop(ComposeService service) =>
		service.Containers.Select(container => LoopOf(container.Id, container.Name)).FirstOrDefault(loop => loop is not null);

	/// <summary>The service's container a shell or the file browser goes to: the first one that runs.</summary>
	private static ComposeContainer? RunningContainer(ComposeService service) =>
		service.Containers.FirstOrDefault(container => container.IsRunning);

	private async Task ShellAsync(string container)
	{
		if (await OpenShellAsync(container) is { } problem)
		{
			Interaction.Notify(Mokaterm.Abstractions.Interaction.NoticeSeverity.Warning, problem);
		}
	}

	private async Task RunAsync(string action) => await RunAsync(action, null);

	private async Task RunAsync(string action, string? service)
	{
		if (_target is not { } target)
		{
			return;
		}

		IReadOnlyList<string> names = [.. _services.Select(item => item.Name)];
		if (ComposeActions.Plan(action, target, names, service, _build, _pullFirst) is not { } plan)
		{
			return;
		}

		if (plan.Live)
		{
			DockerLivePlan live = new(plan.Calls, plan.Title, plan.Confirm ?? plan.Title, plan.ConfirmText, LiveOwner, ErrorIsProgress: true)
			{
				Refreshes = [.. _services.Where(item => service is null || item.Name == service).Select(item => item.Image).Where(image => image.Length > 0)],
			};
			if (await StartLiveAsync(live) is not null)
			{
				_tab = BuildTab;
			}

			return;
		}

		IReadOnlyList<DockerOutput>? outputs = await RunChangesAsync(plan.Calls, plan.Title, plan.Confirm, plan.ConfirmText, RefreshAsync);
		if (action is "update" or "pull" && outputs is { Count: > 0 } && outputs.All(output => output.Succeeded))
		{
			Feature?.ImageUpdates.Forget([.. _services.Where(item => service is null || item.Name == service).Select(item => item.Image)]);
		}
	}

	/// <summary>A row of the services table: a service, or one of a scaled service's containers under it.</summary>
	private sealed record ServiceLine(ComposeService Service, ComposeContainer? Container)
	{
		public string Key => Container is { } container ? $"{Service.Name}/{container.Name}" : Service.Name;
	}
}
