namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>One container that mounts a volume, and where.</summary>
/// <param name="Destination">The path inside the container.</param>
internal sealed record DockerVolumeUse(string Container, string State, string Destination, bool ReadWrite)
{
	/// <summary>
	/// The uses of <paramref name="volume"/> among <paramref name="containers"/> (what <c>container inspect</c> printed
	/// for the containers <c>ps --filter volume=</c> named), one per mount, by container name.
	/// </summary>
	public static IReadOnlyList<DockerVolumeUse> Find(IReadOnlyList<DockerContainerDetails> containers, string volume)
	{
		ArgumentNullException.ThrowIfNull(containers);
		ArgumentNullException.ThrowIfNull(volume);
		return [.. containers
			.SelectMany(container => container.Mounts
				.Where(mount => mount.Type == "volume" && mount.Name == volume)
				.Select(mount => new DockerVolumeUse(container.Name, container.Status, mount.Destination, mount.ReadWrite)))
			.OrderBy(use => use.Container, StringComparer.Ordinal)];
	}
}
