using Renci.SshNet.Messages.Connection;

namespace Mokaterm.Modules.Ssh.Forwarding;

/// <summary>
/// <c>auth-agent-req@openssh.com</c>, which asks the server to offer the agent to programs in this session. It carries
/// nothing past the reply flag, and asks for no reply: SSH.NET waits for the answer to its own pseudo-terminal request on
/// the same channel and would take ours for it.
/// </summary>
internal sealed class AgentForwardingRequest : RequestInfo
{
	public const string Name = "auth-agent-req@openssh.com";

	public AgentForwardingRequest()
	{
		WantReply = false;
	}

	public override string RequestName => Name;
}
