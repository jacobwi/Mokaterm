using System.Globalization;
using Microsoft.AspNetCore.Components;
using Moka.Red.Primitives.DataList;
using Mokaterm.Abstractions.Containers;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.UI.Common.Platform;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// One container beside the list: its state and the facts the list leaves out, the actions used most and the menu with
/// the rest, then its logs, inspect output, stats, environment, mounts and a command box, one tab at a time, with why it
/// keeps restarting first while it is in a restart loop.
/// </summary>
public sealed partial class DockerContainerPanel : DockerManagerPageBase
{
	internal const string RestartsTab = "restarts";

	private static readonly IReadOnlyList<(string Id, string Text)> Tabs =
	[
		("logs", "Logs"),
		("inspect", "Inspect"),
		("stats", "Stats"),
		("env", "Env"),
		("mounts", "Mounts"),
		("run", "Run"),
	];

	private DockerContainerDetails? _details;
	private bool _raw;
	private bool _revealRaw;
	private string? _shellProblem;

	[Parameter, EditorRequired]
	public string ContainerId { get; set; } = "";

	/// <summary>The name the list knows it by, shown until the container's own details arrive.</summary>
	[Parameter]
	public string? DisplayName { get; set; }

	[Parameter]
	public string Tab { get; set; } = "logs";

	[Parameter]
	public EventCallback<string> TabChanged { get; set; }

	[Parameter]
	public EventCallback OnClose { get; set; }

	/// <summary>Raised after a change, so the list behind the panel shows it too.</summary>
	[Parameter]
	public EventCallback OnChanged { get; set; }

	[Inject]
	private IClipboardService Clipboard { get; set; } = default!;

	private string Name => _details?.Name is { Length: > 0 } name ? name : DisplayName is { Length: > 0 } shown ? shown : ContainerId;

	private string? Subtitle => _details is { } details ? StateLine(details) + " · " + MetaLine(details) : null;

	// The tab stays while it is open, so a loop that ends under the user does not take the page away.
	private IReadOnlyList<(string Id, string Text)> ShownTabs =>
		Tab == RestartsTab || LoopOf(ContainerId, _details?.Name) is not null ? [(RestartsTab, "Restarts"), .. Tabs] : Tabs;

	protected override Task OnInitializedAsync()
	{
		FollowRestartLoops();
		return LoadDetailsAsync();
	}

	private static DockerHealth HealthOf(DockerContainerDetails details) => details.Health?.Status ?? DockerHealth.None;

	private static bool IsAlive(DockerContainerDetails details) => details.Status is "running" or "paused" or "restarting";

	private static string Or(string value, string fallback) => value.Length > 0 ? value : fallback;

	private static string When(DateTimeOffset? value) =>
		value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "never";

	private static string HealthText(DockerHealthCheck health) => health.FailingStreak > 0
		? string.Create(CultureInfo.CurrentCulture, $"{DockerStateDisplay.HealthLabel(health.Status).ToLowerInvariant()}, {health.FailingStreak} failing in a row")
		: DockerStateDisplay.HealthLabel(health.Status).ToLowerInvariant();

	/// <summary>Image, short id, compose project and service, and when it started: the facts the list leaves out.</summary>
	private static string MetaLine(DockerContainerDetails details)
	{
		List<string> parts = [details.Image, details.Id.Length > 12 ? details.Id[..12] : details.Id];
		if (details.Labels.TryGetValue(DockerLabels.ComposeProject, out string? project))
		{
			parts.Add(details.Labels.TryGetValue(DockerLabels.ComposeService, out string? service) ? $"{project}/{service}" : project);
		}

		if (details.Status == "running" && details.StartedAt is { } started)
		{
			parts.Add("up since " + When(started));
		}

		return string.Join(" · ", parts);
	}

	private static IReadOnlyList<DockerMount> MountsOf(DockerContainerDetails details) => details.Mounts;

	/// <summary><c>running, healthy</c> or <c>exited 137, killed out of memory</c>.</summary>
	private static string StateLine(DockerContainerDetails details)
	{
		string state = details.Status.Length > 0 ? details.Status : "unknown";
		if (details.Status != "running" && details.ExitCode is { } exit)
		{
			state += string.Create(CultureInfo.InvariantCulture, $" {exit}");
		}

		if (details.Status == "running" && details.Health is { Status: not DockerHealth.None } health)
		{
			state += ", " + DockerStateDisplay.HealthLabel(health.Status).ToLowerInvariant();
		}

		return details.OomKilled ? state + ", killed out of memory" : state;
	}

	private static List<MokaDataListItem> InspectFacts(DockerContainerDetails details)
	{
		List<MokaDataListItem> facts =
		[
			new("Status", details.Status
				+ (details.ExitCode is { } exit && details.Status != "running" ? string.Create(CultureInfo.InvariantCulture, $" (exit {exit})") : "")
				+ (details.OomKilled ? ", killed out of memory" : "")),
		];
		if (details.Error.Length > 0)
		{
			facts.Add(new("Error", details.Error));
		}

		facts.Add(new("Created", When(details.Created)));
		facts.Add(new("Started", When(details.StartedAt)));
		if (details.Status != "running")
		{
			facts.Add(new("Finished", When(details.FinishedAt)));
		}

		facts.Add(new("Restart", string.Create(CultureInfo.CurrentCulture, $"{details.RestartPolicy}, restarted {details.RestartCount} {(details.RestartCount == 1 ? "time" : "times")}")));
		if (details.Health is { } health)
		{
			facts.Add(new("Health", HealthText(health)));
		}

		facts.Add(new("Image", details.Image));
		if (details.Entrypoint.Count > 0)
		{
			facts.Add(new("Entrypoint", string.Join(' ', details.Entrypoint)));
		}

		if (details.Command.Count > 0)
		{
			facts.Add(new("Command", string.Join(' ', details.Command)));
		}

		facts.Add(new("Working dir", Or(details.WorkingDirectory, "/")));
		facts.Add(new("User", Or(details.User, "the image's own (root unless it says otherwise)")));
		facts.Add(new("Hostname", details.Hostname));
		facts.Add(new("Network mode", details.NetworkMode));
		if (details.Pid is { } pid)
		{
			facts.Add(new("PID", pid.ToString(CultureInfo.InvariantCulture)));
		}

		if (details.Privileged)
		{
			facts.Add(new("Privileged", "yes, with every capability and every device"));
		}

		return facts;
	}

	private static List<MokaDataListItem> PortFacts(DockerContainerDetails details) =>
		[.. details.Ports.Select(port => new MokaDataListItem(
			$"{port.ContainerPort}/{port.Protocol}",
			port.HostPort is { } hostPort ? $"{Or(port.HostAddress, "0.0.0.0")}:{hostPort}" : "not published"))];

	private static List<MokaDataListItem> NetworkFacts(DockerContainerDetails details) =>
		[.. details.Networks.Select(network => new MokaDataListItem(
			network.Network,
			string.Join(" · ", new[] { network.IpAddress, network.Ipv6Address, string.Join(", ", network.Aliases) }.Where(part => part.Length > 0))))];

	private static List<MokaDataListItem> LabelFacts(DockerContainerDetails details) =>
		[.. details.Labels.OrderBy(label => label.Key, StringComparer.Ordinal).Select(label => new MokaDataListItem(label.Key, label.Value))];

	private string RawJson(DockerContainerDetails details) => DockerInspectParser.RawJson(details.Raw, _revealRaw);

	private Task LoadDetailsAsync() => LoadAsync(async (client, token) => _details = await client.InspectAsync(ContainerId, token));

	private Task OnTabChangedAsync(string tab) => TabChanged.InvokeAsync(tab);

	private void OnInspectViewChanged(string? value) => _raw = value == "raw";

	private void OnRevealRawChanged(bool value) => _revealRaw = value;

	private async Task ShellAsync() => _shellProblem = await OpenShellAsync(Name);

	private Task BrowseAsync() => BrowseFilesAsync(Name, _details?.WorkingDirectory);

	private async Task AfterChangeAsync()
	{
		await LoadDetailsAsync();
		await OnChanged.InvokeAsync();
	}

	private async Task OnMenuAsync(string action)
	{
		if (_details is not { } details)
		{
			return;
		}

		switch (action)
		{
			case "shell":
				await ShellAsync();
				return;
			case "files":
				await BrowseAsync();
				return;
			case "logs" or "inspect" or "stats" or "mounts" or "run":
				await TabChanged.InvokeAsync(action);
				return;
			case "copy-name":
				await CopyTextAsync(Clipboard, Name, "name");
				return;
			case "copy-id":
				await CopyTextAsync(Clipboard, details.Id, "id");
				return;
		}

		if (DockerContainerActions.Plan(action, Name, IsAlive(details)) is not { } plan)
		{
			return;
		}

		DockerOutput? output = await RunPlanAsync(plan);
		if (output is { Succeeded: true } && action is "remove" or "remove-volumes")
		{
			await OnChanged.InvokeAsync();
			await OnClose.InvokeAsync();
			return;
		}

		await AfterChangeAsync();
	}
}
