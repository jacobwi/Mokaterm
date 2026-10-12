using System.Text;
using Mokaterm.Abstractions.Containers;

namespace Mokaterm.UI.Common.ToolPages;

/// <summary>A change the user ran from a page, kept until the next one so its output and exit stay on screen.</summary>
/// <param name="Title">What it was, for example "Stop web".</param>
/// <param name="Command">The command as shown before it ran.</param>
/// <param name="Output">How it ended, when it ran to its end.</param>
/// <param name="Problem">Why it did not run to its end, when it did not.</param>
internal sealed record DockerChange(string Title, string Command, DockerOutput? Output, string? Problem)
{
	public bool Succeeded => Output is { Succeeded: true };

	public string ExitText => Output?.ExitText ?? "did not finish";

	/// <summary>What the output box shows: both streams, or the reason it did not run.</summary>
	public string Text => Output is { } output
		? output.Combined is { Length: > 0 } combined ? combined : "(no output)"
		: Problem ?? "";

	public bool IsTruncated => Output is { OutputTruncated: true } or { ErrorTruncated: true };

	// The command and its output can both carry a password.
	private bool PrintMembers(StringBuilder builder)
	{
		builder.Append("Title = ").Append(Title).Append(", ExitText = ").Append(ExitText);
		return true;
	}
}
