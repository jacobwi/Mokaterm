namespace Mokaterm.Modules.Ssh.Forwarding;

/// <summary>Where one kind of forwarding stands in a live session, for the port forwarding panel.</summary>
/// <param name="Target">What it forwards to: the agent's source, or the X display.</param>
/// <param name="OpenChannels">Connections from the server being relayed right now.</param>
/// <param name="Problem">The last thing that went wrong, or why it is unavailable.</param>
internal sealed record SshForwardingLine(SshForwardingState State, string Target, int OpenChannels, string? Problem)
{
	public static SshForwardingLine Off { get; } = new(SshForwardingState.Off, "", 0, null);
}
