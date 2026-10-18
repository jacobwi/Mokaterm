using Mokaterm.Abstractions.Containers;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// An image reference read the way docker reads it: <c>nginx</c> is <c>docker.io/library/nginx:latest</c>, a first part
/// with a dot, a colon or <c>localhost</c> is a registry, and a tag follows the last colon after the last slash. Two
/// references that look different (<c>nginx</c>, <c>docker.io/library/nginx:latest</c>) name the same image once read.
/// </summary>
internal sealed record DockerImageName
{
	public const string DefaultRegistry = "docker.io";

	public const string DefaultTag = "latest";

	public required string Registry { get; init; }

	/// <summary>The path in the registry: <c>library/nginx</c>, <c>acme/shop-api</c>.</summary>
	public required string Path { get; init; }

	/// <summary>The tag, which is <c>latest</c> when neither a tag nor a digest was given; null for one pinned by digest only.</summary>
	public string? Tag { get; init; }

	/// <summary><c>sha256:...</c> when the reference pins a digest.</summary>
	public string? Digest { get; init; }

	/// <summary><c>docker.io/library/nginx</c>: the part a <c>RepoDigests</c> entry shares with the tag.</summary>
	public string Repository => $"{Registry}/{Path}";

	/// <summary>The one written form of the reference, for comparing and as a key.</summary>
	public string Canonical => Repository + (Tag is null ? "" : ":" + Tag) + (Digest is null ? "" : "@" + Digest);

	/// <summary>
	/// The reference read, or null for an image id or anything docker would not read as a reference: those name one
	/// image on this host and no registry knows them.
	/// </summary>
	public static DockerImageName? Parse(string? reference)
	{
		string text = reference?.Trim() ?? "";
		if (text.Length == 0 || DockerNames.IsId(text) || DockerNames.ImageProblem(text) is not null)
		{
			return null;
		}

		string? digest = null;
		int at = text.IndexOf('@', StringComparison.Ordinal);
		if (at >= 0)
		{
			digest = text[(at + 1)..];
			text = text[..at];
		}

		string? tag = null;
		int colon = text.LastIndexOf(':');
		if (colon > text.LastIndexOf('/'))
		{
			tag = text[(colon + 1)..];
			text = text[..colon];
		}

		string registry = DefaultRegistry;
		string path = text;
		int slash = text.IndexOf('/', StringComparison.Ordinal);
		if (slash > 0 && IsRegistry(text[..slash]))
		{
			registry = text[..slash];
			path = text[(slash + 1)..];
		}

		if (registry is "index.docker.io" or "registry-1.docker.io")
		{
			registry = DefaultRegistry;
		}

		if (registry == DefaultRegistry && !path.Contains('/', StringComparison.Ordinal))
		{
			path = "library/" + path;
		}

		return new DockerImageName
		{
			Registry = registry,
			Path = path,
			Tag = tag ?? (digest is null ? DefaultTag : null),
			Digest = digest,
		};
	}

	/// <summary>
	/// Why <paramref name="reference"/> cannot name a new tag (what <c>docker build --tag</c> and <c>docker image tag</c>
	/// write), or null when it can: it has to be a reference, not an image id, and pin no digest.
	/// </summary>
	public static string? TagProblem(string? reference)
	{
		string text = reference?.Trim() ?? "";
		if (DockerNames.ImageProblem(text) is { } problem)
		{
			return problem;
		}

		if (DockerNames.IsId(text))
		{
			return "That reads as an image id. Name the image, such as app:1.4 or registry.example.com/team/app:1.4.";
		}

		return Parse(text) is { Digest: null } ? null : "A tag cannot pin a digest. Name the image, such as app:1.4.";
	}

	/// <summary>The canonical form of <paramref name="reference"/>, or the text as it is when it is no reference.</summary>
	public static string Key(string reference)
	{
		ArgumentNullException.ThrowIfNull(reference);
		return Parse(reference)?.Canonical ?? reference.Trim();
	}

	/// <summary>The digest of a <c>RepoDigests</c> entry (<c>nginx@sha256:...</c>) when it belongs to this repository.</summary>
	public string? DigestIn(string repoDigest)
	{
		ArgumentNullException.ThrowIfNull(repoDigest);
		return Parse(repoDigest) is { Digest: { } digest } entry && entry.Repository == Repository ? digest : null;
	}

	private static bool IsRegistry(string part) =>
		part == "localhost" || part.Contains('.', StringComparison.Ordinal) || part.Contains(':', StringComparison.Ordinal) || part.StartsWith('[');
}
