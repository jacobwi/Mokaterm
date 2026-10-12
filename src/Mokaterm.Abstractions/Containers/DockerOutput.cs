using System.Globalization;
using System.Text;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.Abstractions.Containers;

/// <summary>
/// How one docker command ended: the line that ran, its exit, and both streams as text. Prints the sizes and the exit
/// and never the text, because a container's output and its environment can carry a password.
/// </summary>
public sealed record DockerOutput
{
	/// <summary>The command as shown to the user, prefixed with <c>sudo</c> when it ran through sudo.</summary>
	public required string Command { get; init; }

	public int? ExitCode { get; init; }

	/// <summary>The signal that ended the command, when the server reported one instead of an exit code.</summary>
	public string? ExitSignal { get; init; }

	public string Output { get; init; } = "";

	public bool OutputTruncated { get; init; }

	/// <summary>stdout as it came, for a call that asked for bytes rather than text; empty for every other call.</summary>
	public ReadOnlyMemory<byte> RawOutput { get; init; }

	/// <summary>stderr, without what sudo itself printed before the command started.</summary>
	public string Error { get; init; } = "";

	public bool ErrorTruncated { get; init; }

	public bool IsElevated { get; init; }

	public TimeSpan Duration { get; init; }

	public bool Succeeded => ExitCode == 0;

	/// <summary><c>exit 0</c>, <c>exit 125</c>, or the signal that ended it.</summary>
	public string ExitText => ExitCode is { } code
		? string.Create(CultureInfo.InvariantCulture, $"exit {code}")
		: ExitSignal is { Length: > 0 } signal ? $"signal {signal}" : "no exit status";

	/// <summary>Both streams for an output box, stdout first, without escape sequences.</summary>
	public string Combined
	{
		get
		{
			string output = TerminalTextFilter.Plain(Output).TrimEnd();
			string error = TerminalTextFilter.Plain(Error).TrimEnd();
			return output.Length == 0 ? error : error.Length == 0 ? output : output + "\n" + error;
		}
	}

	private bool PrintMembers(StringBuilder builder)
	{
		builder
			.Append("Command = ").Append(SecretText.Describe(Command))
			.Append(", ExitCode = ").Append(ExitCode)
			.Append(", Output = ").Append(Output.Length).Append(" characters")
			.Append(", Error = ").Append(Error.Length).Append(" characters")
			.Append(", IsElevated = ").Append(IsElevated);
		return true;
	}
}
