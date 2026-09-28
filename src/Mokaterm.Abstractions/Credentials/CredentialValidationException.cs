namespace Mokaterm.Abstractions.Credentials;

/// <summary>A credential was rejected before it was saved. The message is written for the user.</summary>
public sealed class CredentialValidationException : Exception
{
	public CredentialValidationException()
	{
	}

	public CredentialValidationException(string message)
		: base(message)
	{
	}

	public CredentialValidationException(string message, Exception innerException)
		: base(message, innerException)
	{
	}

	public CredentialValidationException(PrivateKeyStatus status, string message)
		: base(message)
	{
		Status = status;
	}

	public PrivateKeyStatus? Status { get; }
}
