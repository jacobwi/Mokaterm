namespace Mokaterm.Modules.Ssh.Forwarding;

/// <summary>What answers the agent requests of one forwarded channel: the system agent, or the login's own key.</summary>
internal interface IForwardedAgent
{
	/// <summary>Answers one request body (no length) with one response body.</summary>
	/// <exception cref="Agent.SshAgentException">The agent went away or answered with something that is not the protocol.</exception>
	Task<byte[]> ExchangeAsync(byte[] request, CancellationToken cancellationToken);
}
