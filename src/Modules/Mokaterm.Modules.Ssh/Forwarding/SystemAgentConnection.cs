using Mokaterm.Modules.Ssh.Agent;

namespace Mokaterm.Modules.Ssh.Forwarding;

/// <summary>
/// One connection to the agent on this machine, owned by one forwarded channel the way <c>ssh -A</c> opens one per
/// channel. Every message passes through as it is, so the server's programs can do whatever the agent lets them, adding
/// and removing keys included, exactly as with OpenSSH.
/// </summary>
internal sealed class SystemAgentConnection : IForwardedAgent, IDisposable
{
	private readonly SshAgentClient _client;

	public SystemAgentConnection(SshAgentClient client) => _client = client;

	public Task<byte[]> ExchangeAsync(byte[] request, CancellationToken cancellationToken) =>
		_client.ExchangeAsync(request, AgentFrameReader.MaxMessageBytes, cancellationToken);

	public void Dispose() => _client.Dispose();
}
