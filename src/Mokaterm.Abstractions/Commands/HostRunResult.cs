namespace Mokaterm.Abstractions.Commands;

/// <summary>
/// One login of a run across several logins as it stands: which login it is, where its attempt is and why it ended
/// the way it did. Each kind of run adds what its work produced. A run replaces the whole record on every change, so a
/// view can tell a row that changed from one that did not by reference.
/// </summary>
public abstract record HostRunResult
{
	public required Guid ConnectionId { get; init; }

	/// <summary>The login's own title: its label, or <c>user@address</c>.</summary>
	public required string Title { get; init; }

	/// <summary>The machine's name in the tree, which is its address when it has no name.</summary>
	public required string HostName { get; init; }

	public required string Address { get; init; }

	public string? Username { get; init; }

	public string ProtocolId { get; init; } = "";

	public HostRunState State { get; init; }

	/// <summary>
	/// What the host is doing while it connects ("Authenticating"), and afterwards why it failed, was skipped or was
	/// cancelled. Null once the work ran.
	/// </summary>
	public string? Message { get; init; }

	/// <summary>How long the work took, or how long the attempt took when it never got that far.</summary>
	public TimeSpan? Duration { get; init; }

	/// <summary>How many times this host was started: 1 for the first attempt, more after retries.</summary>
	public int Attempt { get; init; }

	/// <summary>True while the host is queued, connecting or running.</summary>
	public bool IsPending => State is HostRunState.Queued or HostRunState.Connecting or HostRunState.Running;

	/// <summary>Anything a user scanning for trouble wants to see: at the least, a host where the work did not run.</summary>
	public virtual bool IsProblem => State is HostRunState.Failed or HostRunState.Skipped or HostRunState.Cancelled;
}
