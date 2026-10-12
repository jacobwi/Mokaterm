using System.Text.Json;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>One entry of the build cache as <c>system df --verbose</c> lists it.</summary>
internal sealed record DockerBuildCacheEntry
{
	public required string Id { get; init; }

	/// <summary>For example <c>regular</c>, <c>source.local</c> or <c>exec.cachemount</c>.</summary>
	public string CacheType { get; init; } = "";

	/// <summary>The build step that made it, cut by docker to a line.</summary>
	public string Description { get; init; } = "";

	public string Size { get; init; } = "";

	public bool InUse { get; init; }

	public bool Shared { get; init; }

	/// <summary>For example <c>2 weeks ago</c>.</summary>
	public string LastUsedSince { get; init; } = "";

	public long? SizeBytes => DockerSizes.Parse(Size);

	public string ShortId => Id.Length > 12 ? Id[..12] : Id;

	public static DockerBuildCacheEntry? Read(JsonElement item)
	{
		string id = DockerJson.Text(item, "ID");
		return id.Length == 0
			? null
			: new DockerBuildCacheEntry
			{
				Id = id,
				CacheType = DockerJson.Text(item, "CacheType"),
				Description = DockerJson.Text(item, "Description"),
				Size = DockerJson.Text(item, "Size"),
				InUse = DockerJson.Bool(item, "InUse"),
				Shared = DockerJson.Bool(item, "Shared"),
				LastUsedSince = DockerJson.Text(item, "LastUsedSince"),
			};
	}
}
