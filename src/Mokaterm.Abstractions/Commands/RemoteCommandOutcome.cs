namespace Mokaterm.Abstractions.Commands;

/// <summary>A command that ran to its end, whatever it returned.</summary>
public sealed record RemoteCommandOutcome
{
	/// <summary>The exit status, or null when the server reported none, as for a command ended by a signal.</summary>
	public int? ExitCode { get; init; }

	/// <summary>The signal that ended the command (<c>KILL</c>, <c>TERM</c>), when the server reported one.</summary>
	public string? ExitSignal { get; init; }

	public required CapturedOutput Output { get; init; }

	public required CapturedOutput Error { get; init; }

	/// <summary>From the moment the server took the command to the moment it ended.</summary>
	public TimeSpan Duration { get; init; }
}
