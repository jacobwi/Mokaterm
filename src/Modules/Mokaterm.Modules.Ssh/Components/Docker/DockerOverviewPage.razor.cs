using System.Globalization;
using Moka.Red.Primitives.DataList;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.UI.Common.Formatting;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// The engine at a glance: how many containers keep restarting, the counts, versions, what the daemon runs on, the
/// compose it uses, and one row per kind of what <c>docker system df</c> counts, with the way to the cleanup page,
/// which is where anything is removed.
/// </summary>
public sealed partial class DockerOverviewPage : DockerManagerPageBase
{
	private IReadOnlyList<DockerDiskUsage> _usage = [];

	private string? Summary => Feature?.Status is { } status
		? Join(
			status.Version?.ServerVersion is { Length: > 0 } server ? $"Docker {server}" : "Docker",
			status.Info?.Name is { Length: > 0 } name ? $"on {name}" : null,
			status.Info?.OperatingSystem)
		: null;

	/// <summary>What docker system df says could go, all kinds together; null before it answered.</summary>
	private long? Reclaimable => _usage.Count == 0 ? null : _usage.Sum(row => row.ReclaimableBytes ?? 0);

	protected override Task OnInitializedAsync()
	{
		FollowRestartLoops();
		return RefreshAsync();
	}

	private static string Count(int? value) => value?.ToString("N0", CultureInfo.CurrentCulture) ?? "-";

	private static string? ContainerSplit(DockerEngineStatus status) => status.Info is { } info
		? string.Create(CultureInfo.CurrentCulture, $"{info.Running} running, {info.Paused} paused, {info.Stopped} stopped")
		: null;

	private static string Join(params string?[] parts) =>
		string.Join(" · ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));

	private static List<MokaDataListItem> EngineFacts(DockerEngineStatus status) =>
	[
		new("Server", Join(status.Version?.ServerVersion, status.Version?.Platform)),
		new("API", status.Version?.ServerApiVersion ?? ""),
		new("Client", Join(status.Version?.ClientVersion, status.Version?.Context is { Length: > 0 } context ? $"context {context}" : null)),
		new("Host", Join(status.Info?.Name, status.Info?.OperatingSystem)),
		new("Kernel", Join(status.Info?.KernelVersion ?? status.Version?.KernelVersion, status.Info?.Architecture)),
		new("Storage", Join(status.Info?.StorageDriver, status.Info?.RootDirectory)),
		new("Mode", ModeText(status)),
	];

	private List<MokaDataListItem> ComposeFacts(DockerEngineStatus status) =>
	[
		new("Plugin", InstallText(status.Compose.Plugin, "docker compose")),
		new("Standalone", InstallText(status.Compose.Standalone, "docker-compose")),
		new("In use", Feature?.Compose is { } compose ? $"{compose.Display}{(compose.SupportsList ? "" : ", projects from container labels")}" : "none"),
	];

	private static string ModeText(DockerEngineStatus status)
	{
		string daemon = status.Info is { Rootless: true } ? "rootless daemon" : "daemon running as root";
		return status.IsElevated ? $"{daemon}, commands through sudo" : $"{daemon}, commands as the login";
	}

	private static string InstallText(ComposeTool? tool, string command) => tool switch
	{
		null => "not found",
		{ IsLegacy: true } => $"{command} {tool.Version} (Python 1.x: no ls, no JSON)",
		_ => $"{command} {tool.Version}",
	};

	/// <summary>The session's compose choice; the manager remounts its pages for it, so every compose command follows.</summary>
	private void OnPreferenceChanged(string? value)
	{
		if (Feature is { } feature && Enum.TryParse(value, out ComposePreference preference))
		{
			feature.SetComposePreference(preference);
		}
	}

	private Task RefreshAsync() => LoadAsync(async (client, token) =>
	{
		Task<IReadOnlyList<DockerDiskUsage>> usage = client.DiskUsageAsync(token);
		Task detect = Feature is { } feature ? feature.DetectAsync(token) : Task.CompletedTask;
		await Task.WhenAll(usage, detect);
		_usage = await usage;
	});
}
