namespace Mokaterm.Abstractions.Security;

/// <summary>A security key operation did not complete. The message is written for the user.</summary>
public sealed class SecurityKeyException : Exception
{
	public SecurityKeyException()
	{
	}

	public SecurityKeyException(string message)
		: base(message)
	{
	}

	public SecurityKeyException(string message, Exception? innerException)
		: base(message, innerException)
	{
	}

	public SecurityKeyException(SecurityKeyFailure failure, string message, Exception? innerException = null)
		: base(message, innerException)
	{
		Failure = failure;
	}

	public SecurityKeyFailure Failure { get; }
}
