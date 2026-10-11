using System.Globalization;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.UI.Common.Formatting;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// The engine at a glance: versions, what the daemon runs on, counts, and <c>docker system df</c>, with a prune that
/// says exactly what it removes and what <c>df</c> counts as reclaimable before it runs.
/// </summary>
public sealed partial class DockerOverviewPage : DockerPageBase
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

	private Task PruneAsync()
	{
		string reclaimable = string.Join(", ", _usage
			.Where(row => row.Reclaimable.Length > 0 && row.Type != "Local Volumes")
			.Select(row => $"{row.Type.ToLowerInvariant()} {row.Reclaimable}"));

		string message = "Removes every stopped container, every network no container uses, every dangling image (one no tag"
			+ " points at) and all build cache nothing uses. Volumes and tagged images are kept."
			+ (reclaimable.Length > 0 ? $" docker system df puts what this frees at no more than {reclaimable}." : "");

		return ChangeAsync(DockerCommands.SystemPrune(), "Prune Docker", message, "Prune", RefreshAsync);
	}
}
