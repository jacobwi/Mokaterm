namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>Why a docker command did not do what it was asked, as far as its exit and its stderr tell.</summary>
internal enum DockerFailure
{
	/// <summary>Anything the other values do not cover; the message quotes what docker said.</summary>
	Other,

	/// <summary>The login shell found no <c>docker</c> (or no compose).</summary>
	NotInstalled,

	/// <summary>The account may not use the daemon's socket: it is not in the docker group and not root.</summary>
	PermissionDenied,

	/// <summary>The client ran, but no daemon answered on the socket it tried.</summary>
	DaemonUnreachable,

	/// <summary>The container, image, volume or network is gone.</summary>
	NotFound,

	/// <summary>sudo refused to run the command.</summary>
	ElevationFailed,

	/// <summary>The SSH connection closed, or the command ran out of time.</summary>
	ConnectionLost,

	/// <summary>The command printed something this app could not read.</summary>
	UnreadableOutput,
}
