using System.Text.Json;
using Mokaterm.Abstractions.Containers;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>One line of <c>docker volume ls --format '{{json .}}'</c>, with whether any container still uses it.</summary>
internal sealed record DockerVolume
{
	private const string AnonymousLabel = "com.docker.volume.anonymous";

	public required string Name { get; init; }

	public string Driver { get; init; } = "";

	public string Scope { get; init; } = "";

	/// <summary>Where the local driver keeps it on the docker host; empty for drivers that do not say.</summary>
	public string Mountpoint { get; init; } = "";

	/// <summary>False for a volume <c>--filter dangling=true</c> lists: no container, running or not, refers to it.</summary>
	public bool InUse { get; init; } = true;

	/// <summary>Docker's own text for what it holds, which only <c>system df --verbose</c> measures.</summary>
	public string Size { get; init; } = "";

	/// <summary>
	/// Made for an image's VOLUME or an unnamed mount rather than by name: labelled so since Docker 23, and named with 64
	/// hex digits by every version.
	/// </summary>
	public bool IsAnonymous { get; init; }

	public long? SizeBytes => DockerSizes.Parse(Size);

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

		return [.. DockerJsonOutput.ReadObjects(output)
			.Select(item => Read(item, name => !dangling.Contains(name)))
			.OfType<DockerVolume>()];
	}

	/// <summary>The volumes <c>system df --verbose</c> lists, where <c>Links</c> counts the containers that use each.</summary>
	public static IReadOnlyList<DockerVolume> ParseUsage(IEnumerable<JsonElement> items)
	{
		ArgumentNullException.ThrowIfNull(items);

		// A volume whose links docker did not count is taken as used, so nothing is offered for removal on a guess.
		return [.. items.Select(item => Read(item, _ => DockerJson.Int(item, "Links") is not 0)).OfType<DockerVolume>()];
	}

	private static DockerVolume? Read(JsonElement item, Func<string, bool> inUse)
	{
		string name = DockerJson.Text(item, "Name");
		if (name.Length == 0)
		{
			return null;
		}

		return new DockerVolume
		{
			Name = name,
			Driver = DockerJson.Text(item, "Driver"),
			Scope = DockerJson.Text(item, "Scope"),
			Mountpoint = DockerJson.Text(item, "Mountpoint"),
			InUse = inUse(name),
			Size = DockerJson.Text(item, "Size"),
			IsAnonymous = DockerLabels.Parse(DockerJson.Text(item, "Labels")).ContainsKey(AnonymousLabel)
				|| (name.Length == 64 && name.All(char.IsAsciiHexDigitLower)),
		};
	}
}
