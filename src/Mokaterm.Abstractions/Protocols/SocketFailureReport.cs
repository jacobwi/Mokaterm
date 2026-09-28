namespace Mokaterm.Abstractions.Protocols;

/// <summary>What a socket failure means: the failure the shell acts on, and the sentence it shows the user.</summary>
public readonly record struct SocketFailureReport(ConnectFailure Failure, string Message)
{
	/// <summary>
	/// This failure as the exception a module throws. The cause is passed in because a library that wrapped the socket
	/// failure is usually the more useful thing to keep.
	/// </summary>
	public ProtocolConnectException ToException(Exception cause) => new(Failure, Message, cause);
}
