namespace Mokaterm.Modules.Vnc.Protocol;

/// <summary>A server broke the RFB protocol. The message is written for the user.</summary>
internal sealed class VncProtocolException : Exception
{
	public VncProtocolException(string message)
		: base(message)
	{
	}

	public VncProtocolException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
