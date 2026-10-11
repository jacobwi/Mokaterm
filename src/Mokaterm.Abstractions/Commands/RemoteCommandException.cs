namespace Mokaterm.Abstractions.Commands;

/// <summary>A command that did not run to its end, with a message written for the user. It never quotes the command.</summary>
public sealed class RemoteCommandException : Exception
{
	public RemoteCommandException()
	{
	}

	public RemoteCommandException(string message)
		: base(message)
	{
	}

	public RemoteCommandException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
