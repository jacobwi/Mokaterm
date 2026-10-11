namespace Mokaterm.Modules.Ssh.Shell;

/// <summary>
/// A started exec channel as <see cref="CommandExecution"/> needs it. <see cref="RemoteCommand"/> is the real one; the
/// interface is what lets the reading, the caps and the exit handling be tested without a server.
/// </summary>
internal interface IExecChannel : IDisposable
{
	/// <summary>Stdout. Reads return 0 once the command finished and its output was drained.</summary>
	Stream Output { get; }

	/// <summary>Stderr, the same way.</summary>
	Stream ErrorOutput { get; }

	/// <summary>Completes when the channel closes; faults when the connection drops, cancels with the start token.</summary>
	Task Execution { get; }

	int? ExitStatus { get; }

	string? ExitSignal { get; }
}
