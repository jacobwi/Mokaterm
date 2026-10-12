using System.Text.Json;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// <c>docker system df --verbose --format '{{json .}}'</c>: one object holding every image, container, local volume and
/// build cache entry with its size. Each element is the same JSON the listings print, so it is read by their parsers.
/// </summary>
internal sealed record DockerSystemUsage
{
	private static readonly string[] Sections = ["Images", "Containers", "Volumes", "BuildCache"];

	public IReadOnlyList<DockerImage> Images { get; init; } = [];

	public IReadOnlyList<DockerContainer> Containers { get; init; } = [];

	public IReadOnlyList<DockerVolume> Volumes { get; init; } = [];

	public IReadOnlyList<DockerBuildCacheEntry> BuildCache { get; init; } = [];

	/// <summary>Null when the output is not that object: an old docker prints its table whatever the format says.</summary>
	public static DockerSystemUsage? Parse(string? output)
	{
		JsonElement root = DockerJson.First(output);
		if (root.ValueKind != JsonValueKind.Object || !Sections.Any(name => DockerJson.Get(root, name).ValueKind == JsonValueKind.Array))
		{
			return null;
		}

		return new DockerSystemUsage
		{
			Images = DockerImage.Parse(Array(root, "Images")),
			Containers = DockerContainer.Parse(Array(root, "Containers")),
			Volumes = DockerVolume.ParseUsage(Items(root, "Volumes")),
			BuildCache = [.. Items(root, "BuildCache").Select(DockerBuildCacheEntry.Read).OfType<DockerBuildCacheEntry>()],
		};
	}

	private static IReadOnlyList<JsonElement> Items(JsonElement root, string name) =>
		DockerJson.Get(root, name) is { ValueKind: JsonValueKind.Array } array ? [.. array.EnumerateArray()] : [];

	/// <summary>The array as text, for the parsers that read a listing's output.</summary>
	private static string Array(JsonElement root, string name) =>
		DockerJson.Get(root, name) is { ValueKind: JsonValueKind.Array } array ? array.GetRawText() : "";
}
