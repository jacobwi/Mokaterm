using System.Globalization;
using Microsoft.AspNetCore.Components;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.Modules.Ssh.Tools;

namespace Mokaterm.Modules.Ssh.Components.Docker;

public sealed partial class DockerRestartsPanel : DockerManagerPageBase
{
	private readonly LogTail _tail = new();
	private DockerContainerDetails? _details;
	private IReadOnlyList<DockerContainer> _containers = [];
	private string? _logNote;

	[Parameter, EditorRequired]
	public string ContainerId { get; set; } = "";

	[Parameter]
	public EventCallback OnChanged { get; set; }

	private string Name => _details?.Name is { Length: > 0 } name ? name : ContainerId;

	private DockerRestartRecord? Record => RestartLoops?.Find(ContainerId);

	private bool IsLooping => Record is { IsLooping: true };

	private DockerContainer? Container => _containers.FirstOrDefault(container => container.Id == ContainerId);

	private bool CanRecreate => Container is { } container && ComposePlan("recreate", container, _containers) is not null;

	private string LogEmptyText => IsLoading ? "Reading the log..." : "The container logged nothing before it stopped.";

	// The health watch's reads are what move a loop on, so the state is read again in step with them.
	protected override async Task OnInitializedAsync()
	{
		FollowRestartLoops(() => _ = ReadStateAsync());
		await RefreshAsync();
	}

	private static bool IsAlive(DockerContainerDetails details) => details.Status is "running" or "paused" or "restarting";

	private static string When(DateTimeOffset? value) =>
		value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "never";

	private static string ExitText(DockerContainerExit? exit) => exit is null
		? "not seen yet: Docker clears it when it starts the container again"
		: DockerExitCodes.Describe(exit.ExitCode, exit.OomKilled);

	private static string Lines(int count) => count == 1 ? "1 line" : string.Create(CultureInfo.CurrentCulture, $"{count} lines");

	// Docker clears the exit code and the out of memory mark when it starts the container again, so while it runs the
	// last exit is the one the health watch saw.
	private DockerContainerExit? LastExit(DockerContainerDetails details) =>
		details.Status is "running" or "paused" || details.FinishedAt is null
			? Record?.LastExit
			: new DockerContainerExit(details.ExitCode ?? 0, details.OomKilled, details.Error, details.FinishedAt);

	private string RestartsText(DockerContainerDetails details)
	{
		string total = string.Create(CultureInfo.CurrentCulture, $"{details.RestartCount} in all");
		return Record is { RecentRestarts: > 0 } record
			? $"{total}, {DockerRestartWording.Times(record.RecentRestarts)} {DockerRestartWording.Within(record.RecentSpan)}"
			: total;
	}

	private string RunsText(DockerContainerDetails details)
	{
		if (Record is { RecentRuns.Count: > 0 } record)
		{
			return DockerRestartWording.Runs(record.RecentRuns);
		}

		return details is { Status: not "running", StartedAt: { } started, FinishedAt: { } finished } && finished > started
			? DockerRestartWording.RunLength(finished - started)
			: "not measured yet";
	}

	private Task RefreshAsync() => LoadAsync(async (client, token) =>
	{
		DockerContainerDetails details = await client.InspectAsync(ContainerId, token);
		IReadOnlyList<LogLine> lines = await client.LogsAsync(ContainerId, DockerCommands.RestartLogLines, since: null, token);
		_containers = details.Labels.ContainsKey(DockerLabels.ComposeProject) ? await client.ListContainersAsync(token) : [];
		_details = details;
		ShowLogs(lines, details);
	});

	private Task ReadStateAsync() => LoadAsync(async (client, token) => _details = await client.InspectAsync(ContainerId, token));

	private void ShowLogs(IReadOnlyList<LogLine> lines, DockerContainerDetails details)
	{
		if (DockerRestartLoop.RunningAgainSince(details) is not { } since)
		{
			_tail.Reset(lines);
			_logNote = null;
			return;
		}

		IReadOnlyList<LogLine>? before = DockerRestartLoop.BeforeRun(lines, since);
		_tail.Reset(before ?? lines);
		int leftOut = lines.Count - (before?.Count ?? lines.Count);
		_logNote = before is null
			? $"It has run again since {When(since)}, and the last {Lines(lines.Count)} are all from that run."
			: leftOut > 0
				? $"It has run again since {When(since)}. The {Lines(leftOut)} logged since then are left out."
				: null;
	}

	private async Task StopRestartingAsync()
	{
		string name = Name;
		string compose = Container?.ComposeProject is null
			? ""
			: " Compose sets the policy from the project's files again when it makes the container again.";
		await RunChangeAsync(
			DockerCommands.StopRestarting(name),
			$"Stop restarting {name}",
			$"Docker sets the restart policy of {name} to no, so the next time it exits it stays stopped. It is not stopped now, and nothing in it changes.{compose}",
			"Stop restarting",
			AfterChangeAsync,
			destructive: false);
	}

	private async Task StopAsync()
	{
		if (_details is { } details && DockerContainerActions.Plan("stop", Name, IsAlive(details)) is { } plan)
		{
			await RunPlanAsync(plan, AfterChangeAsync);
		}
	}

	private async Task ComposeAsync(string action)
	{
		if (Container is { } container && ComposePlan(action, container, _containers) is { } plan)
		{
			await RunChangesAsync(plan.Calls, plan.Title, plan.Confirm, plan.ConfirmText, AfterChangeAsync);
		}
	}

	private async Task AfterChangeAsync()
	{
		await RefreshAsync();
		await OnChanged.InvokeAsync();
	}
}
