using System.Text.Json;
using Mokaterm.Abstractions.Containers;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// One image of <c>docker image inspect</c>, cut to what an update check reads: its id, its tags, the digests the
/// registries gave it when it was pulled, and the platform it is for.
/// </summary>
internal sealed record DockerLocalImage
{
	public required string Id { get; init; }

	public IReadOnlyList<string> RepoTags { get; init; } = [];

	/// <summary><c>nginx@sha256:...</c> for every registry it came from; empty for an image built or loaded here.</summary>
	public IReadOnlyList<string> RepoDigests { get; init; } = [];

	public string Os { get; init; } = "";

	public string Architecture { get; init; } = "";

	public string Variant { get; init; } = "";

	/// <summary>Every image in the output. An inspect that missed some names still prints the rest, so this reads what is there.</summary>
	public static IReadOnlyList<DockerLocalImage> Parse(string? output) =>
		[.. DockerJsonOutput.ReadObjects(output)
			.Select(item => new DockerLocalImage
			{
				Id = DockerJson.Text(item, "Id"),
				RepoTags = DockerJson.Strings(item, "RepoTags"),
				RepoDigests = DockerJson.Strings(item, "RepoDigests"),
				Os = DockerJson.Text(item, "Os"),
				Architecture = DockerJson.Text(item, "Architecture"),
				Variant = DockerJson.Text(item, "Variant"),
			})
			.Where(image => image.Id.Length > 0)];

	/// <summary>The image <paramref name="name"/> is a tag of, by its canonical form.</summary>
	public static DockerLocalImage? Find(IReadOnlyList<DockerLocalImage> images, DockerImageName name)
	{
		ArgumentNullException.ThrowIfNull(images);
		ArgumentNullException.ThrowIfNull(name);
		return images.FirstOrDefault(image => image.RepoTags.Any(tag => DockerImageName.Parse(tag)?.Canonical == name.Canonical));
	}

	/// <summary>The digests this image has in <paramref name="name"/>'s repository.</summary>
	public IReadOnlyList<string> DigestsFor(DockerImageName name)
	{
		ArgumentNullException.ThrowIfNull(name);
		return [.. RepoDigests.Select(name.DigestIn).OfType<string>()];
	}

	/// <summary>True for a manifest that is for the same platform as this image.</summary>
	public bool IsFor(DockerRegistryPlatform platform)
	{
		ArgumentNullException.ThrowIfNull(platform);
		return string.Equals(platform.Os, Os, StringComparison.Ordinal)
			&& string.Equals(platform.Architecture, Architecture, StringComparison.Ordinal)
			&& (Variant.Length == 0 || platform.Variant.Length == 0 || string.Equals(platform.Variant, Variant, StringComparison.Ordinal));
	}
}
