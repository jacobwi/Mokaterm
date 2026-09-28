namespace Mokaterm.Abstractions.Protocols;

public enum ConnectFailure
{
	Unknown,
	HostUnreachable,
	Timeout,
	AuthenticationFailed,
	HostIdentityRejected,
	Cancelled,
	ProtocolError,
}

/// <summary>A connection failure whose message is written for the user.</summary>
public sealed class ProtocolConnectException : Exception
{
	public ProtocolConnectException()
	{
	}

	public ProtocolConnectException(string message)
		: base(message)
	{
	}

	public ProtocolConnectException(string message, Exception innerException)
		: base(message, innerException)
	{
	}

	public ProtocolConnectException(ConnectFailure failure, string message, Exception? innerException = null)
		: base(message, innerException)
	{
		Failure = failure;
	}

	public ConnectFailure Failure { get; }
}
