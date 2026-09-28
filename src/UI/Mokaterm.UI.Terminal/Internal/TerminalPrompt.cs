using System.Text.RegularExpressions;

namespace Mokaterm.UI.Terminal.Internal;

/// <summary>
/// Pulls the command out of a terminal row by dropping the shell prompt in front of it. Only rows that carry a prompt
/// count as commands, so output lines offer nothing to save.
/// </summary>
internal static partial class TerminalPrompt
{
	/// <summary>Rows longer than this are output (a log line, a base64 blob), not something anyone typed.</summary>
	private const int MaxRowLength = 1024;

	private const int MaxCommandLength = 512;

	private static readonly Regex[] Patterns = [PowerShellPrompt(), WindowsPrompt(), UserHostPrompt(), ArrowPrompt(), BarePrompt()];

	/// <summary>
	/// True when <paramref name="row"/> is a prompt followed by a command, with <paramref name="command"/> set to the
	/// command alone.
	/// </summary>
	public static bool TryExtractCommand(string? row, out string command)
	{
		command = "";
		if (row is null || row.Length is 0 or > MaxRowLength)
		{
			return false;
		}

		string text = row.TrimEnd();
		foreach (Regex pattern in Patterns)
		{
			Match match = pattern.Match(text);
			if (!match.Success)
			{
				continue;
			}

			string candidate = match.Groups[1].Value.Trim();
			if (candidate.Length is > 0 and <= MaxCommandLength)
			{
				command = candidate;
				return true;
			}

			return false;
		}

		return false;
	}

	// PS C:\Users\bc> git status
	[GeneratedRegex(@"^PS\s+[^>]{0,200}>\s*(.+)$", RegexOptions.CultureInvariant, 200)]
	private static partial Regex PowerShellPrompt();

	// C:\Users\bc>dir
	[GeneratedRegex(@"^[A-Za-z]:\\[^>]{0,200}>\s*(.+)$", RegexOptions.CultureInvariant, 200)]
	private static partial Regex WindowsPrompt();

	// user@host:~$ ls, root@box:/var/log# tail -f syslog, [user@host dir]$ ls, (venv) user@host ~ % ls
	[GeneratedRegex(@"^(?:\([^)]{0,60}\)\s*)?\[?[^\s@]{1,64}@[^\r\n]{0,120}?[$#%]\s+(.+)$", RegexOptions.CultureInvariant, 200)]
	private static partial Regex UserHostPrompt();

	// The oh-my-zsh and starship arrows, with an optional directory between the arrow and the command.
	[GeneratedRegex(@"^[\u279c\u276f\u00bb\u25b6]\s+(?:[^\s]{1,80}\s+)?(.+)$", RegexOptions.CultureInvariant, 200)]
	private static partial Regex ArrowPrompt();

	// A prompt trimmed down to its last character, and the continuation prompt of a multi-line command.
	[GeneratedRegex(@"^[$#%>]\s+(.+)$", RegexOptions.CultureInvariant, 200)]
	private static partial Regex BarePrompt();
}
