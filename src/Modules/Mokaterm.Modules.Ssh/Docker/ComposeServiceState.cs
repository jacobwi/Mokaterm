namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>How one compose service is doing, from its containers.</summary>
internal enum ComposeServiceState
{
	/// <summary>Every container is running and none reports unhealthy.</summary>
	Running,

	/// <summary>Some containers run and some do not.</summary>
	Partial,

	/// <summary>No container runs, and none ended with an error.</summary>
	Stopped,

	/// <summary>A container ended with a non-zero exit code or keeps restarting.</summary>
	Failed,

	/// <summary>A running container's health check fails.</summary>
	Unhealthy,

	/// <summary>The files define the service, but it has no container.</summary>
	NoContainer,
}
