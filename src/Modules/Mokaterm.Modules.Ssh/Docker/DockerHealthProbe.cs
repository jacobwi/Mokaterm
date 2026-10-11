using System.Text;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>One run of a health check: when it started, what it exited with and the start of what it printed.</summary>
internal sealed record DockerHealthProbe(DateTimeOffset? Start, int ExitCode, string Output)
{
	// What a check printed is the container's own output, which this app never logs.
	private bool PrintMembers(StringBuilder builder)
	{
		builder.Append("Start = ").Append(Start).Append(", ExitCode = ").Append(ExitCode);
		return true;
	}
}
