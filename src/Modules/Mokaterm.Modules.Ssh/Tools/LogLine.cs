using System.Text;

namespace Mokaterm.Modules.Ssh.Tools;

/// <summary>One line of a log a session tool shows: a container's, a compose project's, the journal's, a file's.</summary>
/// <param name="Timestamp">
/// The line's own RFC 3339 timestamp, as the tool printed it, or empty. Docker's carry nine fraction digits and the
/// journal's an offset, and either sorts as text against others of its kind.
/// </param>
/// <param name="Text">The line without its timestamp, escape sequences and control characters.</param>
/// <param name="IsError">True for a line the program wrote to stderr.</param>
/// <remarks>Public only because <see cref="Components.LogView"/> is a component and takes a <see cref="LogTail"/>.</remarks>
public sealed record LogLine(string Timestamp, string Text, bool IsError)
{
	/// <summary>Where a merged log's line came from (a project's container), or empty.</summary>
	public string Source { get; init; } = "";

	// A log line is the server's own output, which this app never logs.
	private bool PrintMembers(StringBuilder builder)
	{
		builder.Append("Timestamp = ").Append(Timestamp).Append(", IsError = ").Append(IsError).Append(", Source = ").Append(Source);
		return true;
	}
}
