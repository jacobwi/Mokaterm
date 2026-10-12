using System.Text.Json;
using Mokaterm.Abstractions.Containers;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// What a registry holds for a tag right now, as <c>docker buildx imagetools inspect</c> or
/// <c>docker manifest inspect --verbose</c> printed it: the tag's own digest (an index for a multi-platform image), and
/// each platform's manifest where the tool lists them.
/// </summary>
internal sealed record DockerRegistryManifest
{
	/// <summary>The digest the tag points at, which is what a pull records in <c>RepoDigests</c>; null when the tool does not print it.</summary>
	public string? Digest { get; init; }

	public IReadOnlyList<DockerRegistryPlatform> Platforms { get; init; } = [];

	/// <summary>
	/// The plain text <c>imagetools inspect</c> prints, which every buildx release has printed the same way: the tag's
	/// <c>Digest:</c> line at the top, then for an index a <c>Manifests:</c> list of <c>Name: ...@sha256:...</c> and
	/// <c>Platform: linux/amd64</c> pairs. Null when there is no digest line.
	/// </summary>
	public static DockerRegistryManifest? ParseImagetools(string? output)
	{
		string? digest = null;
		List<DockerRegistryPlatform> platforms = [];
		string? manifest = null;
		foreach (string raw in (output ?? "").Split('\n'))
		{
			string line = raw.TrimEnd('\r');
			string trimmed = line.TrimStart();
			bool indented = trimmed.Length < line.Length;
			if (!indented && Value(trimmed, "Digest:") is { } top && digest is null)
			{
				digest = top;
			}
			else if (indented && Value(trimmed, "Name:") is { } name)
			{
				int at = name.LastIndexOf('@');
				manifest = at >= 0 ? name[(at + 1)..] : null;
			}
			else if (indented && Value(trimmed, "Platform:") is { } platform && manifest is not null)
			{
				platforms.Add(DockerRegistryPlatform.FromText(platform, manifest));
				manifest = null;
			}
		}

		return IsDigest(digest) ? new DockerRegistryManifest { Digest = digest, Platforms = platforms } : null;
	}

	/// <summary>
	/// The JSON <c>manifest inspect --verbose</c> prints: one object for a single manifest, whose descriptor carries the
	/// tag's digest, or an array with one object per platform for an index, whose own digest it never prints. Null when
	/// it is neither.
	/// </summary>
	public static DockerRegistryManifest? ParseManifestVerbose(string? output)
	{
		if (DockerJsonOutput.TryParse((output ?? "").Trim()) is not { } root || root.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
		{
			return null;
		}

		List<DockerRegistryPlatform> platforms = [];
		foreach (JsonElement entry in DockerJsonOutput.ReadObjects(output))
		{
			JsonElement descriptor = DockerJson.Get(entry, "Descriptor");
			string digest = DockerJson.Text(descriptor, "digest");
			if (!IsDigest(digest))
			{
				continue;
			}

			JsonElement platform = DockerJson.Get(descriptor, "platform");
			string config = DockerJson.Text(DockerJson.Get(entry, "SchemaV2Manifest", "config"), "digest");
			if (config.Length == 0)
			{
				config = DockerJson.Text(DockerJson.Get(entry, "OCIManifest", "config"), "digest");
			}

			platforms.Add(new DockerRegistryPlatform(
				DockerJson.Text(platform, "os"),
				DockerJson.Text(platform, "architecture"),
				DockerJson.Text(platform, "variant"),
				digest,
				IsDigest(config) ? config : null));
		}

		if (platforms.Count == 0)
		{
			return null;
		}

		return new DockerRegistryManifest
		{
			Digest = root.ValueKind == JsonValueKind.Object ? platforms[0].Digest : null,
			Platforms = platforms,
		};
	}

	/// <summary>The manifest for the platform <paramref name="image"/> runs on, if this lists one.</summary>
	public DockerRegistryPlatform? PlatformOf(DockerLocalImage image)
	{
		ArgumentNullException.ThrowIfNull(image);
		return Platforms.FirstOrDefault(image.IsFor);
	}

	private static string? Value(string line, string key) =>
		line.StartsWith(key, StringComparison.Ordinal) ? line[key.Length..].Trim() : null;

	private static bool IsDigest(string? value) =>
		value is { Length: > 7 } && value.StartsWith("sha256:", StringComparison.Ordinal) && value[7..].All(char.IsAsciiHexDigitLower);
}
