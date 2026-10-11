namespace Mokaterm.Abstractions.Commands;

/// <summary>Where one host of a command run is.</summary>
public enum CommandHostState
{
	/// <summary>Waiting for a free slot under the run's parallel limit.</summary>
	Queued,

	Connecting,

	Running,

	/// <summary>The command ran to its end, whatever it returned: a non-zero exit is still done.</summary>
	Done,

	/// <summary>The host could not be reached or logged in to, the command could not run, or the time ran out.</summary>
	Failed,

	/// <summary>Connecting would have asked something and the run skips hosts that ask.</summary>
	Skipped,

	Cancelled,
}
