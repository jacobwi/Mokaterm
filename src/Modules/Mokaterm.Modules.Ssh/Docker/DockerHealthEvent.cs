namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>What the health watch tells the user about.</summary>
internal enum DockerHealthEvent
{
	/// <summary>A running container's health check started failing.</summary>
	TurnedUnhealthy,

	/// <summary>A container stopped without anyone asking it to.</summary>
	Exited,

	/// <summary>The daemon restarted a container under its restart policy.</summary>
	Restarted,
}
