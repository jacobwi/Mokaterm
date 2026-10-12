using System.Text;
using Mokaterm.Abstractions.Credentials;

namespace Mokaterm.Abstractions.Commands;

/// <summary>One command for <see cref="IRemoteCommandFeature"/>. The command line is printed as "(set)" only, since it can carry a token.</summary>
public sealed record RemoteCommandRequest
{
	/// <summary>The smallest <see cref="OutputLimitBytes"/>: less would cut most error messages short.</summary>
	public const int MinOutputLimitBytes = 1024;

	/// <summary>The command line as the login shell on the other side reads it. It may span lines.</summary>
	public required string Command { get; init; }

	/// <summary>How much of each stream, stdout and stderr, is kept. The rest is counted in <see cref="CapturedOutput.TotalBytes"/>.</summary>
	public int OutputLimitBytes { get; init; } = 256 * 1024;

	/// <summary>
	/// The command may run for minutes (an install, an image pull). A connection that limits how many commands run at
	/// once keeps some room for quick ones, which such a command never takes.
	/// </summary>
	public bool RunsLong { get; init; }

	/// <summary>The command with the blank space around it removed, which is what gets sent.</summary>
	public static string Normalize(string? command) => command?.Trim() ?? "";

	/// <summary>
	/// Why <paramref name="command"/> cannot be sent, or null when it can. Tabs and line breaks are part of a script; any
	/// other control character is refused because a shell would read it as something other than what the box showed,
	/// and a NUL would cut the command short on the server.
	/// </summary>
	public static string? Problem(string? command)
	{
		string text = Normalize(command);
		if (text.Length == 0)
		{
			return "Type a command to run.";
		}

		foreach (char character in text)
		{
			if (char.IsControl(character) && character is not ('\t' or '\n' or '\r'))
			{
				return "The command holds a control character other than a tab or a line break.";
			}
		}

		return null;
	}

	private bool PrintMembers(StringBuilder builder)
	{
		builder
			.Append("Command = ").Append(SecretText.Describe(Command))
			.Append(", OutputLimitBytes = ").Append(OutputLimitBytes)
			.Append(", RunsLong = ").Append(RunsLong);
		return true;
	}
}
