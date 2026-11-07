namespace Mokaterm.Abstractions.Protocols;

/// <summary>How one step of a connect attempt stands.</summary>
public enum ConnectStepState
{
	/// <summary>Not reached yet, or never reached because the attempt stopped before it.</summary>
	Pending,

	/// <summary>Under way.</summary>
	Running,

	/// <summary>Finished, and it held.</summary>
	Done,

	/// <summary>The attempt broke here.</summary>
	Failed,

	/// <summary>Does not apply to this attempt, such as a login check the server chose not to run.</summary>
	Skipped,
}
