namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>One mount of a container, from <c>docker inspect</c>.</summary>
/// <param name="Type"><c>volume</c>, <c>bind</c>, <c>tmpfs</c> or <c>npipe</c>.</param>
/// <param name="Name">The volume's name; empty for a bind mount.</param>
/// <param name="Source">The path on the docker host.</param>
/// <param name="Destination">The path inside the container.</param>
public sealed record DockerMount(string Type, string Name, string Source, string Destination, string Mode, bool ReadWrite);
