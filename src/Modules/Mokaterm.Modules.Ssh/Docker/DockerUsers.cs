using Mokaterm.Abstractions.Containers;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// Which containers use an image or a volume, worked out from the container listing the pages read anyway, so saying
/// it costs no command of its own. Stopped containers count: they still hold what they were made from.
/// </summary>
internal static class DockerUsers
{
	/// <summary>
	/// The names of the containers made from <paramref name="image"/>: by its reference read the way docker reads one
	/// (<c>nginx</c> is <c>nginx:latest</c>), or by its id, which is what <c>docker ps</c> shows once the tag moved on.
	/// </summary>
	public static IReadOnlyList<string> OfImage(DockerImage image, IReadOnlyList<DockerContainer> containers)
	{
		ArgumentNullException.ThrowIfNull(image);
		ArgumentNullException.ThrowIfNull(containers);
		string? reference = image.IsTagged ? DockerImageName.Key(image.Reference) : null;
		string id = image.Id.StartsWith("sha256:", StringComparison.Ordinal) ? image.Id[7..] : image.Id;
		return [.. containers
			.Where(container => (reference is not null && DockerImageName.Key(container.Image) == reference) || IsIdOf(container.Image, id))
			.Select(container => container.Name)
			.Order(StringComparer.OrdinalIgnoreCase)];
	}

	/// <summary>The names of the containers that mount each volume, by volume name.</summary>
	public static IReadOnlyDictionary<string, IReadOnlyList<string>> OfVolumes(IReadOnlyList<DockerContainer> containers)
	{
		ArgumentNullException.ThrowIfNull(containers);
		Dictionary<string, List<string>> users = new(StringComparer.Ordinal);
		foreach (DockerContainer container in containers)
		{
			foreach (string mount in container.MountNames.Distinct(StringComparer.Ordinal))
			{
				if (!users.TryGetValue(mount, out List<string>? names))
				{
					users[mount] = names = [];
				}

				names.Add(container.Name);
			}
		}

		return users.ToDictionary(
			pair => pair.Key,
			pair => (IReadOnlyList<string>)[.. pair.Value.Order(StringComparer.OrdinalIgnoreCase)],
			StringComparer.Ordinal);
	}

	private static bool IsIdOf(string reference, string id)
	{
		if (!DockerNames.IsId(reference))
		{
			return false;
		}

		string hex = reference.StartsWith("sha256:", StringComparison.Ordinal) ? reference[7..] : reference;
		return id.StartsWith(hex, StringComparison.Ordinal);
	}
}
