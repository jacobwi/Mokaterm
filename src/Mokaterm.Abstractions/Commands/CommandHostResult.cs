namespace Mokaterm.Abstractions.Commands;

/// <summary>One host of a command run: the login's row, and what its command returned and printed.</summary>
public sealed record CommandHostResult : HostRunResult
{
	public int? ExitCode { get; init; }

	public string? ExitSignal { get; init; }

	public CommandOutputText Output { get; init; } = CommandOutputText.Empty;

	public CommandOutputText Error { get; init; } = CommandOutputText.Empty;

	/// <summary>A host that did not run the command, or ran it and got a non-zero exit.</summary>
	public override bool IsProblem => base.IsProblem || (State == HostRunState.Done && ExitCode != 0);
}
