namespace Mokaterm.Abstractions.Commands;

/// <summary>
/// One host of a command run as it stands. A run replaces the whole record on every change, so a view can tell a row
/// that changed from one that did not by reference.
/// </summary>
public sealed record CommandHostResult
{
	public required Guid ConnectionId { get; init; }

	/// <summary>The login's own title: its label, or <c>user@address</c>.</summary>
	public required string Title { get; init; }

	/// <summary>The machine's name in the tree, which is its address when it has no name.</summary>
	public required string HostName { get; init; }

	public required string Address { get; init; }

	public string? Username { get; init; }

	public string ProtocolId { get; init; } = "";

	public CommandHostState State { get; init; }

	/// <summary>
	/// What the host is doing while it connects ("Authenticating"), and afterwards why it failed, was skipped or was
	/// cancelled. Null once the command ran.
	/// </summary>
	public string? Message { get; init; }

	public int? ExitCode { get; init; }

	public string? ExitSignal { get; init; }

	/// <summary>How long the command ran, or how long the attempt took when it never got that far.</summary>
	public TimeSpan? Duration { get; init; }

	public CommandOutputText Output { get; init; } = CommandOutputText.Empty;

	public CommandOutputText Error { get; init; } = CommandOutputText.Empty;

	/// <summary>How many times this host was started: 1 for the first run, more after retries.</summary>
	public int Attempt { get; init; }

	/// <summary>True while the host is queued, connecting or running.</summary>
	public bool IsPending => State is CommandHostState.Queued or CommandHostState.Connecting or CommandHostState.Running;

	/// <summary>Anything a user scanning for trouble wants to see: a host that did not run the command, or ran it and got a non-zero exit.</summary>
	public bool IsProblem =>
		State is CommandHostState.Failed or CommandHostState.Skipped or CommandHostState.Cancelled
		|| (State == CommandHostState.Done && ExitCode != 0);
}
