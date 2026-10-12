using Mokaterm.Modules.Ssh.Tunnels;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// The tunnels the Docker manager's Open adds: session-only, from a free port on 127.0.0.1 to where the container is
/// published. A tunnel already open to the same place is used again, saved ones included, so opening a page twice does
/// not stack tunnels; one of ours that closed or failed opens again rather than leaving a dead row beside a new one.
/// </summary>
internal static class DockerPortTunnels
{
	/// <summary>
	/// An open tunnel to <paramref name="host"/>:<paramref name="port"/>, or the status of the attempt to open one, whose
	/// <see cref="SshTunnelStatus.Error"/> says why it failed.
	/// </summary>
	/// <param name="name">What the tunnel is for, shown in the port forwarding view.</param>
	/// <exception cref="ArgumentException">The destination is not one a tunnel can go to.</exception>
	public static async Task<SshTunnelStatus> OpenAsync(ISshTunnelFeature tunnels, string host, int port, string name, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(tunnels);
		ArgumentNullException.ThrowIfNull(host);
		ArgumentNullException.ThrowIfNull(name);
		IReadOnlyList<SshTunnelStatus> current = tunnels.Tunnels;
		if (current.FirstOrDefault(status => status is { State: SshTunnelState.Open, BoundPort: not null } && Reaches(status.Tunnel, host, port)) is { } open)
		{
			return open;
		}

		if (current.FirstOrDefault(status => IsOurs(status, host, port) && status.State is SshTunnelState.Closed or SshTunnelState.Failed) is { } idle)
		{
			return await tunnels.OpenAsync(idle.Tunnel.Id, cancellationToken);
		}

		SshTunnelDefinition tunnel = new()
		{
			Id = Guid.NewGuid(),
			Kind = SshTunnelKind.Local,
			Name = name.Length <= SshTunnelDefinition.MaxNameLength ? name : name[..SshTunnelDefinition.MaxNameLength],
			ListenHost = SshTunnelDefinition.DefaultListenHost,
			ListenPort = 0,
			DestinationHost = host,
			DestinationPort = port,
		};

		return await tunnels.AddAsync(tunnel, cancellationToken);
	}

	/// <summary>A local tunnel this machine's browser can use at 127.0.0.1 that comes out at the same place.</summary>
	private static bool Reaches(SshTunnelDefinition tunnel, string host, int port) =>
		tunnel.Kind == SshTunnelKind.Local
		&& tunnel.ListenHost is SshTunnelDefinition.DefaultListenHost or "localhost" or "0.0.0.0"
		&& tunnel.DestinationPort == port
		&& SameHost(tunnel.DestinationHost, host);

	private static bool IsOurs(SshTunnelStatus status, string host, int port) =>
		status.IsSessionOnly
		&& status.Tunnel is { ListenPort: 0, ListenHost: SshTunnelDefinition.DefaultListenHost }
		&& Reaches(status.Tunnel, host, port);

	// The editor's own default destination is "localhost", which is where a port published on every interface goes too.
	private static bool SameHost(string left, string right) =>
		string.Equals(Loopback(left), Loopback(right), StringComparison.OrdinalIgnoreCase);

	private static string Loopback(string host) =>
		string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ? DockerPortAccess.AnyInterfaceTarget : host;
}
