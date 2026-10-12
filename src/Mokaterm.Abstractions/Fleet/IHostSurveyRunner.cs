namespace Mokaterm.Abstractions.Fleet;

/// <summary>
/// Reads the summary of several saved logins' machines without opening a tab for any of them. One per UI scope, like
/// <see cref="Commands.ICommandRunner"/>, whose connect and limits it shares: credentials come from that scope's vault,
/// prompts go to that scope's window, and disposing the scope ends every survey and closes its connections.
/// </summary>
public interface IHostSurveyRunner
{
	/// <summary>
	/// Reads the logins and starts <paramref name="request"/>; hosts connect in the background and report through the
	/// returned survey. The caller owns the survey and disposes it once its results are no longer shown.
	/// </summary>
	/// <exception cref="ArgumentException">No login was given.</exception>
	/// <exception cref="ObjectDisposedException">The scope is going away.</exception>
	Task<IHostSurvey> StartAsync(HostSurveyRequest request, CancellationToken cancellationToken = default);
}
