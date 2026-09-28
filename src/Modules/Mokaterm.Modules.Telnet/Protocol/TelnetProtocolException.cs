namespace Mokaterm.Modules.Telnet.Protocol;

/// <summary>A telnet connection that broke while it was running, with a message written for the user.</summary>
internal sealed class TelnetProtocolException : Exception
{
	public TelnetProtocolException()
	{
	}

	public TelnetProtocolException(string message)
		: base(message)
	{
	}

	public TelnetProtocolException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
