using System.Text;

namespace Mokaterm.Abstractions.ServerAdmin;

/// <summary>A System view script that ran to its end, whatever it returned. Prints its sizes, never its output.</summary>
public sealed record SystemScriptResult
{
	public int? ExitCode { get; init; }

	/// <summary>The signal that ended the script, when the server reported one instead of an exit code.</summary>
	public string? ExitSignal { get; init; }

	/// <summary>Stdout after the script's output marker, so nothing a login printed in front of it is here.</summary>
	public string Output { get; init; } = "";

	/// <summary>Stderr from the script itself; what sudo printed before it ran is not here.</summary>
	public string Error { get; init; } = "";

	/// <summary>The output passed the script's cap; <see cref="Output"/> ends at the last whole line that fit.</summary>
	public bool IsTruncated { get; init; }

	public TimeSpan Duration { get; init; }

	/// <summary>The first line of stderr, or a sentence naming the exit code when there is none: what a page shows when the script failed.</summary>
	public string Problem { get; init; } = "";

	public bool Succeeded => ExitCode == 0;

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
