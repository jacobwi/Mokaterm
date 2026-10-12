namespace Mokaterm.Abstractions.Containers;

/// <summary>
/// A docker command that did not run to its end, or a read that failed, with a message written for the user. It never
/// quotes the command line; what the command printed, when it ran at all, rides along so a page can show it.
/// </summary>
public class DockerOperationException : Exception
{
	public DockerOperationException()
	{
	}

	public DockerOperationException(string message)
		: base(message)
	{
	}

	public DockerOperationException(string message, Exception innerException)
		: base(message, innerException)
	{
	}

	public DockerOperationException(string message, DockerOutput? output, Exception? innerException = null)
		: base(message, innerException)
	{
		Output = output;
	}

	/// <summary>What the command printed, when it ran at all.</summary>
	public DockerOutput? Output { get; }
}
