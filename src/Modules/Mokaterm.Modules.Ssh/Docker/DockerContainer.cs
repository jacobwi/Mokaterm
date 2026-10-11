using System.Text;
using Mokaterm.Abstractions.Containers;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>A container as the list shows it: the <c>docker ps</c> row with its ports, labels and compose membership read out.</summary>
internal sealed record DockerContainer
{
	public required DockerContainerRow Row { get; init; }

	public IReadOnlyList<DockerPort> Ports { get; init; } = [];

	public IReadOnlyDictionary<string, string> Labels { get; init; } = new Dictionary<string, string>();

	public DockerHealth Health { get; init; }

	public string Id => Row.Id;

	/// <summary>The first 12 characters of the id, as docker shows it.</summary>
	public string ShortId => Row.Id.Length > 12 ? Row.Id[..12] : Row.Id;

	public string Name => Row.Name;

	public string Image => Row.Image;

	public string State => Row.State;

	public string Status => Row.Status;

	public bool IsRunning => Row.State == "running";

	public bool IsPaused => Row.State == "paused";

	/// <summary>True while the container has a process: running, paused or restarting.</summary>
	public bool IsAlive => Row.State is "running" or "paused" or "restarting";

	public string? ComposeProject => Label(DockerLabels.ComposeProject);

	public string? ComposeService => Label(DockerLabels.ComposeService);

	public string? ComposeWorkingDirectory => Label(DockerLabels.ComposeWorkingDirectory);

	public IReadOnlyList<string> ComposeConfigFiles => DockerLabels.SplitList(Label(DockerLabels.ComposeConfigFiles));

	/// <summary>The <c>--env-file</c> files its project was started with, when its compose recorded them.</summary>
	public IReadOnlyList<string> ComposeEnvironmentFiles => DockerLabels.SplitList(Label(DockerLabels.ComposeEnvironmentFile));

	/// <summary>True for a container <c>compose run</c> started, which belongs to no service.</summary>
	public bool IsOneOff => string.Equals(Label(DockerLabels.ComposeOneOff), "True", StringComparison.OrdinalIgnoreCase);

	public IReadOnlyList<string> NetworkNames => DockerLabels.SplitList(Row.Networks);

	public IReadOnlyList<string> MountNames => DockerLabels.SplitList(Row.Mounts);

	public static IReadOnlyList<DockerContainer> Parse(string? output) =>
		[.. DockerContainerRow.Parse(output).Select(FromRow)];

	public static DockerContainer FromRow(DockerContainerRow row)
	{
		ArgumentNullException.ThrowIfNull(row);
		return new DockerContainer
		{
			Row = row,
			Ports = DockerPorts.Parse(row.Ports),
			Labels = DockerLabels.Parse(row.Labels),
			Health = HealthFromStatus(row.Status),
		};
	}

	/// <summary><c>Up 2 hours (healthy)</c>, <c>(unhealthy)</c> or <c>(health: starting)</c>.</summary>
	public static DockerHealth HealthFromStatus(string? status)
	{
		string text = status ?? "";
		if (text.Contains("(unhealthy)", StringComparison.OrdinalIgnoreCase))
		{
			return DockerHealth.Unhealthy;
		}

		if (text.Contains("(healthy)", StringComparison.OrdinalIgnoreCase))
		{
			return DockerHealth.Healthy;
		}

		return text.Contains("health: starting", StringComparison.OrdinalIgnoreCase) ? DockerHealth.Starting : DockerHealth.None;
	}

	/// <summary>True when <paramref name="query"/> appears in the name, the image, the id, the ports or the compose labels.</summary>
	public bool Matches(string query)
	{
		ArgumentNullException.ThrowIfNull(query);
		string needle = query.Trim();
		return needle.Length == 0
			|| Name.Contains(needle, StringComparison.OrdinalIgnoreCase)
			|| Image.Contains(needle, StringComparison.OrdinalIgnoreCase)
			|| Id.StartsWith(needle, StringComparison.OrdinalIgnoreCase)
			|| Row.Ports.Contains(needle, StringComparison.OrdinalIgnoreCase)
			|| (ComposeProject?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false)
			|| (ComposeService?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false);
	}

	private string? Label(string key) => Labels.TryGetValue(key, out string? value) && value.Length > 0 ? value : null;

	private bool PrintMembers(StringBuilder builder)
	{
		builder.Append("Row = ").Append(Row);
		return true;
	}
}
