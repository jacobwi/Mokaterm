namespace Mokaterm.Modules.Rdp.Protocol;

/// <summary>The server refused the login. Another password may work, so the provider asks again.</summary>
internal sealed class RdpAuthenticationException : Exception
{
	public RdpAuthenticationException(string message)
		: base(message)
	{
	}

	public RdpAuthenticationException(string message, Exception? innerException)
		: base(message, innerException)
	{
	}
}
