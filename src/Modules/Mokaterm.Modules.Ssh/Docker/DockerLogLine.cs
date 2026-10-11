using System.Text;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>One line of a container's log.</summary>
/// <param name="Timestamp">Docker's own timestamp (RFC 3339 with nine fraction digits, so it sorts as text), or empty.</param>
/// <param name="Text">The line without its timestamp, escape sequences and control characters.</param>
/// <param name="IsError">True for a line the container wrote to stderr.</param>
public sealed record DockerLogLine(string Timestamp, string Text, bool IsError)
{
	/// <summary>The container a merged project log line came from, or empty for a single container's log.</summary>
	public string Source { get; init; } = "";

	// A log line is the container's own output, which this app never logs.
	private bool PrintMembers(StringBuilder builder)
	{
		builder.Append("Timestamp = ").Append(Timestamp).Append(", IsError = ").Append(IsError).Append(", Source = ").Append(Source);
		return true;
	}
}
