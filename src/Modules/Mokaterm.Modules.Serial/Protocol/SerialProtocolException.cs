namespace Mokaterm.Modules.Serial.Protocol;

/// <summary>A serial session that ended on its own, with a message written for the user.</summary>
internal sealed class SerialProtocolException : Exception
{
	public SerialProtocolException()
	{
	}

	public SerialProtocolException(string message)
		: base(message)
	{
	}

	public SerialProtocolException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
