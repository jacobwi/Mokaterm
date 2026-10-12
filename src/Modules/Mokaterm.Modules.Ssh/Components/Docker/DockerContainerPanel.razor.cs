using System.Globalization;
using Microsoft.AspNetCore.Components;
using Mokaterm.Abstractions.Containers;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.UI.Common.Platform;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// One container under the list: a header with its state, the two actions used most and the menu with the rest, then
/// its logs, inspect output, stats, environment, mounts and a command box, one tab at a time.
/// </summary>
public sealed partial class DockerContainerPanel : DockerManagerPageBase
{
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

	private string Name => _details?.Name is { Length: > 0 } name ? name : ContainerId;

	protected override Task OnInitializedAsync() => LoadDetailsAsync();

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
