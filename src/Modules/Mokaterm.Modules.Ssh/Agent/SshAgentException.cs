namespace Mokaterm.Modules.Ssh.Agent;

/// <summary>An agent could not be reached, refused a request or answered with something that is not the protocol.</summary>
internal sealed class SshAgentException : Exception
{
	public SshAgentException()
	{
	}

	public SshAgentException(string message)
		: base(message)
	{
	}

	public SshAgentException(string message, Exception? innerException)
		: base(message, innerException)
	{
	}
}
