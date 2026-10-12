using System.Text;
using Mokaterm.Modules.Ssh.Shell;

namespace Mokaterm.Modules.Ssh.Admin;

/// <summary>A system view script that ran to its end, whatever it returned. Prints its sizes, never its output.</summary>
internal sealed record AdminOutcome
{
	public int? ExitCode { get; init; }

	public string? ExitSignal { get; init; }

	/// <summary>Stdout after the script's output marker, so login noise in front of it is gone.</summary>
	public string Output { get; init; } = "";

	/// <summary>Stderr from the script itself; what sudo printed before it ran is not here.</summary>
	public string Error { get; init; } = "";

	/// <summary>The output passed the script's cap; <see cref="Output"/> ends at the last whole line that fit.</summary>
	public bool IsTruncated { get; init; }

	public TimeSpan Duration { get; init; }

	public bool Succeeded => ExitCode == 0;

	/// <summary>The first line of stderr, or a sentence naming the exit code when there is none.</summary>
	public string Problem()
	{
		if (RemoteText.FirstLine(Error) is { Length: > 0 } line)
		{
			return line;
		}

		return ExitCode is { } code ? $"The command ended with exit code {code}." : $"The command was ended by {ExitSignal ?? "a signal"}.";
	}

	private bool PrintMembers(StringBuilder builder)
	{
		builder
			.Append("ExitCode = ").Append(ExitCode)
			.Append(", Output = ").Append(Output.Length)
			.Append(", Error = ").Append(Error.Length)
			.Append(", IsTruncated = ").Append(IsTruncated);
		return true;
	}
}
