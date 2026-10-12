using Mokaterm.Modules.Ssh.Shell;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// Decides whether an image is behind its tag, and reads why a registry could not be asked. A pull records the
/// digest the tag pointed at in <c>RepoDigests</c>, so the image is current exactly while the registry's digest for the
/// tag is still one of them; nothing is pulled to find out.
/// </summary>
internal static class ImageUpdateCheck
{
	private static readonly string[] UnsupportedMarkers =
	[
		"is not a docker command",
		"unknown command",
		"experimental cli features",
		"unknown flag",
	];

	private static readonly string[] RefusedMarkers =
	[
		"unauthorized",
		"denied",
		"authentication required",
		"no basic auth credentials",
		"403 forbidden",
	];

	private static readonly string[] UnreachableMarkers =
	[
		"no such host",
		"connection refused",
		"i/o timeout",
		"tls handshake timeout",
		"network is unreachable",
		"context deadline exceeded",
		"server misbehaving",
		"dial tcp",
		"x509:",
	];

	private static readonly string[] MissingMarkers =
	[
		"manifest unknown",
		"no such manifest",
		"not found",
	];

	/// <summary>A result for an image that has no registry digest, or no tag, without asking anyone.</summary>
	public static ImageUpdateResult? WithoutRegistry(string reference, DockerImageName? name, DockerLocalImage? image, DateTimeOffset now)
	{
		ArgumentNullException.ThrowIfNull(reference);
		if (name is not { Tag: { } })
		{
			return new ImageUpdateResult(reference, ImageUpdateState.NoTag, "It has no tag to look up in a registry.", now);
		}

		if (image is null)
		{
			return new ImageUpdateResult(reference, ImageUpdateState.NoTag, $"No image on this host is tagged {reference} any more.", now);
		}

		return image.DigestsFor(name).Count == 0
			? new ImageUpdateResult(reference, ImageUpdateState.LocalOnly, "It was built on this host or loaded from a file, so no registry has it to compare with.", now)
			: null;
	}

	/// <summary>Compares what the host pulled with what the registry holds for the tag now.</summary>
	public static ImageUpdateResult Compare(string reference, DockerImageName name, DockerLocalImage image, DockerRegistryManifest remote, DateTimeOffset now)
	{
		ArgumentNullException.ThrowIfNull(reference);
		ArgumentNullException.ThrowIfNull(name);
		ArgumentNullException.ThrowIfNull(image);
		ArgumentNullException.ThrowIfNull(remote);
		IReadOnlyList<string> pulled = image.DigestsFor(name);
		if (remote.Digest is { } digest)
		{
			return pulled.Contains(digest, StringComparer.Ordinal) ? Current(reference, name, now, digest) : Newer(reference, name, now, digest);
		}

		// docker manifest prints each platform of an index but not the index itself, so the platform this image runs on
		// is compared instead: its manifest against RepoDigests, or its config against the image id the classic store uses.
		if (remote.PlatformOf(image) is not { } platform)
		{
			return new ImageUpdateResult(reference, ImageUpdateState.TagGone, $"{name.Tag} on the registry has no {image.Os}/{image.Architecture} image any more.", now);
		}

		if (pulled.Contains(platform.Digest, StringComparer.Ordinal) || string.Equals(platform.ConfigDigest, image.Id, StringComparison.Ordinal))
		{
			return Current(reference, name, now, platform.Digest);
		}

		// The containerd store names an image by its index, which docker manifest never prints, so no answer is honest.
		return pulled.Contains(image.Id, StringComparer.Ordinal)
			? new ImageUpdateResult(reference, ImageUpdateState.Failed, "This docker keeps images by their index digest, which docker manifest does not print. With buildx installed it can be checked.", now)
			: Newer(reference, name, now, platform.Digest);
	}

	/// <summary>True when the tool itself is missing or too old, rather than the registry saying no.</summary>
	public static bool IsUnsupported(DockerOutput output)
	{
		ArgumentNullException.ThrowIfNull(output);
		return output.ExitCode == DockerErrors.CommandNotFoundExitStatus || ContainsAny(output.Error, UnsupportedMarkers);
	}

	/// <summary>Why a registry tool failed for <paramref name="reference"/>, as a result.</summary>
	public static ImageUpdateResult Failed(string reference, DockerImageName name, DockerOutput output, string host, DateTimeOffset now)
	{
		ArgumentNullException.ThrowIfNull(reference);
		ArgumentNullException.ThrowIfNull(name);
		ArgumentNullException.ThrowIfNull(output);
		string error = output.Error.Length > 0 ? output.Error : output.Output;
		string said = RemoteText.FirstLine(error);
		(ImageUpdateState state, string detail) = error switch
		{
			_ when IsUnsupported(output) => (ImageUpdateState.Unsupported, Unsupported(host)),
			_ when ContainsAny(error, RefusedMarkers) => (ImageUpdateState.Refused, $"The registry would not say: the image is private, or {host} needs docker login for {name.Registry}."),
			_ when ContainsAny(error, UnreachableMarkers) => (ImageUpdateState.Unreachable, $"{host} could not reach {name.Registry}. {said}".TrimEnd()),
			_ when ContainsAny(error, MissingMarkers) => (ImageUpdateState.TagGone, $"{name.Registry} has no {name.Path}:{name.Tag} any more."),
			_ => (ImageUpdateState.Failed, said.Length > 0 ? said : "The registry check failed without saying why."),
		};

		return new ImageUpdateResult(reference, state, detail, now);
	}

	/// <summary>The sentence for a host whose docker can ask no registry.</summary>
	public static string Unsupported(string host) =>
		$"The docker on {host} has neither buildx nor docker manifest (Docker 20.10 and later have the second), so it cannot ask a registry.";

	private static ImageUpdateResult Current(string reference, DockerImageName name, DateTimeOffset now, string digest) =>
		new(reference, ImageUpdateState.UpToDate, $"{name.Tag} on {name.Registry} is still the image this host has.", now) { RemoteDigest = digest };

	private static ImageUpdateResult Newer(string reference, DockerImageName name, DateTimeOffset now, string digest) =>
		new(reference, ImageUpdateState.UpdateAvailable, $"{name.Tag} on {name.Registry} is a newer image ({Short(digest)}). Pulling fetches it; containers keep the old one until they are recreated.", now) { RemoteDigest = digest };

	private static string Short(string digest) => digest.Length > 19 ? digest[..19] : digest;

	private static bool ContainsAny(string text, string[] markers) =>
		markers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));
}
