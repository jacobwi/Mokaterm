namespace Mokaterm.Modules.Rdp.Sessions;

/// <summary>
/// A running session that stopped against its will. The message is written for the user: the tab shows it as the reason
/// the session dropped, and the shell offers to reconnect.
/// </summary>
internal sealed class RdpSessionException : Exception
{
	public RdpSessionException(string message)
		: base(message)
	{
	}

	public RdpSessionException(string message, Exception? innerException)
		: base(message, innerException)
	{
	}
}
