namespace Mokaterm.Modules.Ssh.Tunnels;

public enum SshTunnelState
{
	Closed,
	Opening,
	Open,
	Failed,
}

/// <summary>What one tunnel is doing right now.</summary>
public sealed record SshTunnelStatus
{
	public required SshTunnelDefinition Tunnel { get; init; }

	public SshTunnelState State { get; init; }

	/// <summary>Why the tunnel is not open, for example a port already in use. Null unless <see cref="State"/> is Failed.</summary>
	public string? Error { get; init; }

	/// <summary>
	/// The port the tunnel really listens on, which is the one the operating system picked when the definition asked
	/// for 0. Null while the tunnel is not open.
	/// </summary>
	public int? BoundPort { get; init; }

	/// <summary>True for tunnels added to the running session only, which are gone after a reconnect.</summary>
	public bool IsSessionOnly { get; init; }

	/// <summary>The endpoints with the port the tunnel actually took.</summary>
	public string Describe() =>
		BoundPort is { } bound && Tunnel.ListenPort == 0 ? (Tunnel with { ListenPort = bound }).Describe() : Tunnel.Describe();
}
