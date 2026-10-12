namespace Mokaterm.Abstractions.Commands;

/// <summary>Where one host of a run across several logins is: a command run or a survey of the machines.</summary>
public enum HostRunState
{
	/// <summary>Waiting for a free slot under the run's parallel limit.</summary>
	Queued,

	Connecting,

	/// <summary>Connected, and the command or the survey is under way.</summary>
	Running,

	/// <summary>The work ran to its end, whatever it returned: a command with a non-zero exit is still done.</summary>
	Done,

	/// <summary>The host could not be reached or logged in to, the work could not run, or the time ran out.</summary>
	Failed,

	/// <summary>Connecting would have asked something and the run skips hosts that ask.</summary>
	Skipped,

	Cancelled,
}
