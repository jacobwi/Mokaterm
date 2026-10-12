using Mokaterm.Abstractions.Containers;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// A docker command that failed, with a message written for the user. It never quotes the command line; the output,
/// when there is one, rides along so the page can show what docker printed. Pages from other modules see it as the
/// <see cref="DockerOperationException"/> it is.
/// </summary>
internal sealed class DockerCommandException : DockerOperationException
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
		: base(message, output, innerException)
	{
		Failure = failure;
	}

	public DockerFailure Failure { get; } = DockerFailure.Other;
}
