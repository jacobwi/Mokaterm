namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>One network a container is attached to, from <c>docker inspect</c>.</summary>
internal sealed record DockerNetworkAttachment(
	string Network,
	string IpAddress,
	string Gateway,
	string MacAddress,
	string Ipv6Address,
	IReadOnlyList<string> Aliases);
