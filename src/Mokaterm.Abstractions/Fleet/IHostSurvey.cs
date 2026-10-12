using Mokaterm.Abstractions.Commands;

namespace Mokaterm.Abstractions.Fleet;

/// <summary>
/// Several saved logins read for their machine's summary, through the same per-host connect, prompt rule, time limit
/// and parallel limit a command run uses. It lives as long as the view that shows it, and nothing it read is kept
/// anywhere else.
/// </summary>
public interface IHostSurvey : IHostRun<HostSurveyResult>
{
	/// <summary>
	/// Reads <paramref name="connectionIds"/> again, adding any login the survey did not have yet at the end. A login
	/// still being read is left alone. The logins are read afresh from the catalog, so one changed in the meantime is
	/// used as it is now. Returns how many were queued.
	/// </summary>
	/// <param name="askPrompts">True asks whatever a connect needs; false skips such a host as the survey's options say.</param>
	Task<int> RefreshAsync(IReadOnlyCollection<Guid> connectionIds, bool askPrompts = false, CancellationToken cancellationToken = default);
}
