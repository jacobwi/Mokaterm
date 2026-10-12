namespace Mokaterm.Abstractions.ServerAdmin;

/// <summary>
/// A System view script that did not run to its end, with a message written for the user: the connection went away,
/// sudo refused, the time ran out, or the server never started the script. A script that ran and failed is not this;
/// its exit code and output come back in a <see cref="SystemScriptResult"/>.
/// </summary>
public class SystemScriptException : Exception
{
	public SystemScriptException()
	{
	}

	public SystemScriptException(string message)
		: base(message)
	{
	}

	public SystemScriptException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
