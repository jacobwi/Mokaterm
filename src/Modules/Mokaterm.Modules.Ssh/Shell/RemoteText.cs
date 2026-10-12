using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.Modules.Ssh.Shell;

/// <summary>What a remote command printed, quoted inside a sentence for the user.</summary>
internal static class RemoteText
{
	private const int MaxQuoteLength = 300;

	/// <summary>
	/// The first line that says something: plain text, trimmed, and cut short when a tool printed a page on one line.
	/// Empty when there is none.
	/// </summary>
	public static string FirstLine(string? text)
	{
		foreach (string line in TerminalTextFilter.Plain(text ?? "").Split('\n'))
		{
			string trimmed = line.Trim();
			if (trimmed.Length > 0)
			{
				return trimmed.Length > MaxQuoteLength ? trimmed[..MaxQuoteLength] + "..." : trimmed;
			}
		}

		return "";
	}
}
