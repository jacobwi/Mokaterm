namespace Mokaterm.Modules.Ssh.Tunnels;

/// <summary>Which way a forwarded port points.</summary>
public enum SshTunnelKind
{
	/// <summary>Listens here and forwards to a host the server can reach. <c>ssh -L</c>.</summary>
	Local,

	/// <summary>Listens on the server and forwards to a host this machine can reach. <c>ssh -R</c>.</summary>
	Remote,

	/// <summary>A SOCKS proxy here that the server resolves and connects for. <c>ssh -D</c>.</summary>
	Dynamic,
}
