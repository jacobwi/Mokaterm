using System.Text;

namespace Mokaterm.Modules.Ssh.Docker;

internal sealed record DockerContainerExit(int ExitCode, bool OomKilled, string Error, DateTimeOffset? FinishedAt)
{
	// Docker's error text names paths and hosts, which this app never logs.
	private bool PrintMembers(StringBuilder builder)
	{
		builder.Append("ExitCode = ").Append(ExitCode).Append(", OomKilled = ").Append(OomKilled).Append(", FinishedAt = ").Append(FinishedAt);
		return true;
	}
}
