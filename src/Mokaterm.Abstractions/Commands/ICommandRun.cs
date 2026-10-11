namespace Mokaterm.Abstractions.Commands;

/// <summary>
/// One command running on several saved logins, each over a connection of its own that no tab shows. Every connection
/// closes when its command ends, when the run is cancelled and when the run is disposed.
/// </summary>
public interface ICommandRun : IAsyncDisposable
{
	/// <summary>The command as it was sent. It lives in memory only, for as long as the run does.</summary>
	string Command { get; }

	/// <summary>One result per login, in the order the request listed them. A new list on every change.</summary>
	IReadOnlyList<CommandHostResult> Hosts { get; }

	/// <summary>True while any host is queued, connecting or running.</summary>
	bool IsRunning { get; }

	/// <summary>Raised after any host changed. Handlers may run on any thread.</summary>
	event Action? Changed;

	/// <summary>Stops every host still queued, connecting or running. Hosts that finished keep their results.</summary>
	void CancelAll();

	/// <summary>
	/// Runs the failed and cancelled hosts again with the same options, reading their logins afresh so a login fixed
	/// in the meantime is used as it is now. Returns how many were queued.
	/// </summary>
	Task<int> RetryFailedAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Runs the skipped hosts again, this time asking whatever their connect needs. Returns how many were queued. The
	/// prompts still come one at a time, which is why a run only does this when asked.
	/// </summary>
	Task<int> RetrySkippedAsync(CancellationToken cancellationToken = default);

	/// <summary>Completes once nothing is queued or running. A retry after that starts a new wait.</summary>
	Task WaitAsync(CancellationToken cancellationToken = default);
}
