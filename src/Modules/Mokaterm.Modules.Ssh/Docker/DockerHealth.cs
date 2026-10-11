namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>What a container's health check last said, for containers that have one.</summary>
internal enum DockerHealth
{
	/// <summary>No health check, or none reported.</summary>
	None,

	Starting,

	Healthy,

	Unhealthy,
}
