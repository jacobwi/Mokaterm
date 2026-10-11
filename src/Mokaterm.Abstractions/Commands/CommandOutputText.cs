using System.Text;

namespace Mokaterm.Abstractions.Commands;

/// <summary>
/// One stream of a command's output as plain text for the screen: escape sequences and control characters other than
/// tabs and line breaks are gone, and only the part the run kept is here. Prints its sizes and never its text.
/// </summary>
public sealed record CommandOutputText(string Text, long TotalBytes, bool IsTruncated)
{
	public static CommandOutputText Empty { get; } = new("", 0, false);

	private bool PrintMembers(StringBuilder builder)
	{
		builder
			.Append("Characters = ").Append(Text.Length)
			.Append(", TotalBytes = ").Append(TotalBytes)
			.Append(", IsTruncated = ").Append(IsTruncated);
		return true;
	}
}
