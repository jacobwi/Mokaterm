namespace Mokaterm.Abstractions.Commands;

/// <summary>
/// Runs one command on several saved logins without opening a tab for any of them. One per UI scope: credentials come
/// from that scope's vault and prompts go to that scope's window, and disposing the scope cancels every run still going
/// and closes every connection a run opened.
/// </summary>
public interface ICommandRunner
{
	/// <summary>
	/// Reads the logins and starts <paramref name="request"/>; hosts connect in the background and report through the
	/// returned run. The caller owns the run and disposes it once its results are no longer shown.
	/// </summary>
	/// <exception cref="ArgumentException">The command cannot be sent (<see cref="RemoteCommandRequest.Problem"/>), or no login was given.</exception>
	/// <exception cref="ObjectDisposedException">The scope is going away.</exception>
	Task<ICommandRun> StartAsync(CommandRunRequest request, CancellationToken cancellationToken = default);
}
