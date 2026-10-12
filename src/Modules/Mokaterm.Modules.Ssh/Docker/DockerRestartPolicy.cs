namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>What the daemon does when a new container's process ends: docker's four <c>--restart</c> policies.</summary>
internal enum DockerRestartPolicy
{
	/// <summary>Never; docker's default, so no <c>--restart</c> is passed.</summary>
	No,

	/// <summary>Only after a non-zero exit, as often as the retry count allows.</summary>
	OnFailure,

	/// <summary>Whenever it stops, and again when the daemon starts.</summary>
	Always,

	/// <summary>Like always, except after the user stopped it.</summary>
	UnlessStopped,
}
