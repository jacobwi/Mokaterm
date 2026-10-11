namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// A docker command that failed, with a message written for the user. It never quotes the command line; the output,
/// when there is one, rides along so the page can show what docker printed.
/// </summary>
internal sealed class DockerCommandException : Exception
{
	public DockerCommandException()
	{
	}

	public DockerCommandException(string message)
		: base(message)
	{
	}

	public DockerCommandException(string message, Exception innerException)
		: base(message, innerException)
	{
	}

	public DockerCommandException(DockerFailure failure, string message, DockerOutput? output = null, Exception? innerException = null)
		: base(message, innerException)
	{
		Failure = failure;
		Output = output;
	}

	public DockerFailure Failure { get; } = DockerFailure.Other;

	/// <summary>What the command printed, when it ran at all.</summary>
	public DockerOutput? Output { get; }
}
