namespace Mokaterm.Modules.Ssh.Tunnels;

/// <summary>
/// The forwarded ports of one live SSH session, as the tunnels view sees them. Reached through
/// <c>IProtocolSession.GetFeature&lt;ISshTunnelFeature&gt;()</c>.
/// </summary>
public interface ISshTunnelFeature
{
	/// <summary>Raised after any tunnel changes state. Handlers may run on any thread.</summary>
	event Action? Changed;

	/// <summary>Every tunnel this session knows, saved ones first, in the order they were added.</summary>
	IReadOnlyList<SshTunnelStatus> Tunnels { get; }

	/// <summary>How many tunnels are open right now.</summary>
	int OpenCount { get; }

	/// <summary>Adds a tunnel to the running session only. It is not saved with the connection.</summary>
	/// <returns>The tunnel's status after the attempt, which may be <see cref="SshTunnelState.Failed"/>.</returns>
	Task<SshTunnelStatus> AddAsync(SshTunnelDefinition tunnel, CancellationToken cancellationToken = default);

	/// <summary>Opens a tunnel that is closed or failed. Failures land in the status, not in an exception.</summary>
	Task<SshTunnelStatus> OpenAsync(Guid tunnelId, CancellationToken cancellationToken = default);

	/// <summary>Closes an open tunnel. Existing connections through it are dropped.</summary>
	Task<SshTunnelStatus> CloseAsync(Guid tunnelId, CancellationToken cancellationToken = default);

	/// <summary>Closes a tunnel and forgets it. Saved tunnels come back on the next connect.</summary>
	Task RemoveAsync(Guid tunnelId, CancellationToken cancellationToken = default);
}
