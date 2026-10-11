using System.Text.Json;
using Mokaterm.Abstractions.Containers;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>One line of <c>docker volume ls --format '{{json .}}'</c>, with whether any container still uses it.</summary>
internal sealed record DockerVolume
{
	public required string Name { get; init; }

	public string Driver { get; init; } = "";

	public string Scope { get; init; } = "";

	/// <summary>Where the local driver keeps it on the docker host; empty for drivers that do not say.</summary>
	public string Mountpoint { get; init; } = "";

	/// <summary>False for a volume <c>--filter dangling=true</c> lists: no container, running or not, refers to it.</summary>
	public bool InUse { get; init; } = true;

	public bool Matches(string query)
	{
		ArgumentNullException.ThrowIfNull(query);
		string needle = query.Trim();
		return needle.Length == 0
			|| Name.Contains(needle, StringComparison.OrdinalIgnoreCase)
			|| Driver.Contains(needle, StringComparison.OrdinalIgnoreCase)
			|| Mountpoint.Contains(needle, StringComparison.OrdinalIgnoreCase);
	}

	/// <param name="output">The listing.</param>
	/// <param name="unused">The names <c>docker volume ls -q --filter dangling=true</c> printed, one per line.</param>
	public static IReadOnlyList<DockerVolume> Parse(string? output, string? unused)
	{
		HashSet<string> dangling = new(
			(unused ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
			StringComparer.Ordinal);

		List<DockerVolume> volumes = [];
		foreach (JsonElement item in DockerJsonOutput.ReadObjects(output))
		{
			string name = DockerJson.Text(item, "Name");
			if (name.Length == 0)
			{
				continue;
			}

			volumes.Add(new DockerVolume
			{
				Name = name,
				Driver = DockerJson.Text(item, "Driver"),
				Scope = DockerJson.Text(item, "Scope"),
				Mountpoint = DockerJson.Text(item, "Mountpoint"),
				InUse = !dangling.Contains(name),
			});
		}

		return volumes;
	}
}
