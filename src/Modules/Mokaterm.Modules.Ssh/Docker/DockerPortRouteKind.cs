namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>How the browser gets to a published port.</summary>
internal enum DockerPortRouteKind
{
	/// <summary>Through a local tunnel of the session, on the desktop, where the browser runs where the connection starts.</summary>
	Tunnel,

	/// <summary>Straight at the session's host, on the web host, for a port published on every interface.</summary>
	Direct,

	/// <summary>Not from here; <see cref="DockerPortRoute.Reason"/> says why.</summary>
	Refused,
}
