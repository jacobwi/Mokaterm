using System.Globalization;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.UI.Common.Formatting;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// The engine at a glance: versions, what the daemon runs on, counts, and one line per kind of what <c>docker system
/// df</c> counts, with the way to the cleanup page, which is where anything is removed.
/// </summary>
public sealed partial class DockerOverviewPage : DockerManagerPageBase
{
	private IReadOnlyList<DockerDiskUsage> _usage = [];

	protected override Task OnInitializedAsync() => RefreshAsync();

	private static string Count(int? value) => value?.ToString("N0", CultureInfo.CurrentCulture) ?? "-";

	private static string ContainerSplit(DockerEngineStatus status) => status.Info is { } info
		? string.Create(CultureInfo.CurrentCulture, $"{info.Running} running, {info.Paused} paused, {info.Stopped} stopped")
		: "";

	private static string Join(string? first, string? second) =>
		string.Join(" · ", new[] { first, second }.Where(part => !string.IsNullOrWhiteSpace(part)));

	private static string ModeText(DockerEngineStatus status)
	{
		string daemon = status.Info is { Rootless: true } ? "rootless daemon" : "daemon running as root";
		return status.IsElevated ? $"{daemon}, commands through sudo" : $"{daemon}, commands as the login";
	}

	/// <summary><c>4.2GB in 9, 1.1GB (26%) reclaimable</c>.</summary>
	private static string UsageLine(DockerDiskUsage row) =>
		row.Reclaimable.Length > 0 && row.ReclaimableBytes is > 0
			? $"{row.Size} in {row.TotalCount}, {row.Reclaimable} reclaimable"
			: $"{row.Size} in {row.TotalCount}";

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
