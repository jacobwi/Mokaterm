namespace Mokaterm.Modules.Vnc.Protocol;

/// <summary>
/// The server refused the login. The connector catches it to ask for another password, so it must never be used for
/// failures another password cannot fix.
/// </summary>
internal sealed class VncAuthenticationException : Exception
{
	public VncAuthenticationException(string message)
		: base(message)
	{
	}

	public VncAuthenticationException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
