using System.Text.Json;
using Mokaterm.Abstractions.Containers;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>One line of <c>docker image ls --format '{{json .}}'</c>: one tag of an image, or an image with no tag at all.</summary>
internal sealed record DockerImage
{
	private const string None = "<none>";

	public required string Id { get; init; }

	public string Repository { get; init; } = "";

	public string Tag { get; init; } = "";

	public string Digest { get; init; } = "";

	/// <summary>Docker's own text, for example <c>187MB</c>.</summary>
	public string Size { get; init; } = "";

	public string CreatedSince { get; init; } = "";

	public DateTimeOffset? CreatedAt { get; init; }

	/// <summary>How many containers, running or not, were made from it; only <c>system df --verbose</c> counts them.</summary>
	public int? ContainerCount { get; init; }

	/// <summary>What removing it frees: the layers no other image shares. Only <c>system df --verbose</c> prints it.</summary>
	public string UniqueSize { get; init; } = "";

	/// <summary>An image no tag points at any more: what <c>docker image prune</c> removes.</summary>
	public bool IsDangling => Repository is "" or None && Tag is "" or None;

	/// <summary>True when a tag points at it, so a registry can be asked about it.</summary>
	public bool IsTagged => Repository is not ("" or None) && Tag is not ("" or None);

	/// <summary><c>nginx:1.27</c>, <c>nginx@sha256:...</c> for an image kept by digest, or the short id for a dangling one.</summary>
	public string Reference
	{
		get
		{
			if (IsDangling || Repository is "" or None)
			{
				return ShortId;
			}

			if (Tag is "" or None)
			{
				return Digest is "" or None ? Repository : $"{Repository}@{Digest}";
			}

			return $"{Repository}:{Tag}";
		}
	}

	/// <summary>The 12 hex digits docker shows, without the <c>sha256:</c> in front.</summary>
	public string ShortId
	{
		get
		{
			string hex = Id.StartsWith("sha256:", StringComparison.Ordinal) ? Id[7..] : Id;
			return hex.Length > 12 ? hex[..12] : hex;
		}
	}

	public long? SizeBytes => DockerSizes.Parse(Size);

	/// <summary>The unique size where docker printed one, else the whole size.</summary>
	public long? FreedBytes => DockerSizes.Parse(UniqueSize) ?? SizeBytes;

	/// <summary>
	/// What <c>docker image rm</c> is given: the tag, so removing one tag of an image with several leaves the rest, or the
	/// id for an image without one.
	/// </summary>
	public string RemoveTarget => IsDangling || Repository is "" or None || Tag is "" or None ? Id : Reference;

	public bool Matches(string query)
	{
		ArgumentNullException.ThrowIfNull(query);
		string needle = query.Trim();
		return needle.Length == 0
			|| Reference.Contains(needle, StringComparison.OrdinalIgnoreCase)
			|| ShortId.StartsWith(needle, StringComparison.OrdinalIgnoreCase);
	}

	public static IReadOnlyList<DockerImage> Parse(string? output)
	{
		List<DockerImage> images = [];
		foreach (JsonElement item in DockerJsonOutput.ReadObjects(output))
		{
			string id = DockerJson.Text(item, "ID");
			if (id.Length == 0)
			{
				continue;
			}

			images.Add(new DockerImage
			{
				Id = id,
				Repository = DockerJson.Text(item, "Repository"),
				Tag = DockerJson.Text(item, "Tag"),
				Digest = DockerJson.Text(item, "Digest"),
				Size = DockerJson.Text(item, "Size"),
				CreatedSince = DockerJson.Text(item, "CreatedSince"),
				CreatedAt = DockerJson.ParseListingTime(DockerJson.Text(item, "CreatedAt")),
				ContainerCount = DockerJson.Int(item, "Containers"),
				UniqueSize = DockerJson.Text(item, "UniqueSize"),
			});
		}

		return images;
	}
}
