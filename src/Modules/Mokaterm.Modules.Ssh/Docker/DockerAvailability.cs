namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>What looking for Docker on the server found.</summary>
internal enum DockerAvailability
{
	/// <summary>The client ran and the daemon answered.</summary>
	Ready,

	/// <summary>There is no docker command for this login.</summary>
	NotInstalled,

	/// <summary>The daemon's socket refused this account.</summary>
	PermissionDenied,

	/// <summary>The client ran, but no daemon answered.</summary>
	DaemonUnreachable,

	/// <summary>Something else went wrong; the status carries docker's message.</summary>
	Failed,
}
