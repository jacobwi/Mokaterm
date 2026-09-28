using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Transfers;

namespace Mokaterm.Maui.Services;

/// <summary>
/// The session managers and transfer queues of the open pages, so closing the window can stop them. MAUI releases a
/// page's services without waiting when its window closes and the process ends first, so connections would drop without
/// a goodbye and a download cut off halfway would stay at its final path, looking complete. The page's services are
/// out of reach from the window by then: BlazorWebView.TryDispatchAsync queues its callback behind the closing window.
/// </summary>
public sealed class DesktopSessions
{
	// Long enough for servers to take a goodbye and canceled downloads to clean up; one that never answers should not
	// keep the process around, nor hold up a restart.
	private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(3);

	private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

	private readonly Lock _lock = new();
	private readonly List<Registration> _registrations = [];
	private readonly ISettingsService _settings;

	public DesktopSessions(ISettingsService settings) => _settings = settings;

	/// <summary>Called by the page's root component; disposing the result forgets the page again.</summary>
	public IDisposable Track(ISessionManager sessions, ITransferQueue transfers)
	{
		Registration registration = new(this, sessions, transfers);
		lock (_lock)
		{
			_registrations.Add(registration);
		}

		return registration;
	}

	/// <summary>
	/// The one way this app goes away: every session closed and every pending setting written, under one deadline. Both
	/// callers end the process right after (the window being destroyed, and Velopack restarting into a new version), and
	/// neither can wait on a server or a disk that never answers. The work goes to the thread pool and nothing of it
	/// comes back to the caller's thread, so the window close handler can block on it while it holds the UI thread.
	/// Failures are recorded and swallowed: there is nothing left to do about them.
	/// </summary>
	public Task ShutDownAsync() => Task.Run(async () =>
	{
		// Both at once: a session close that runs its whole deadline must not be what loses the settings.
		Task stop = Report(StopAllAsync);
		Task flush = Report(() => _settings.FlushAsync());
		_ = await Task.WhenAny(Task.WhenAll(stop, flush), Task.Delay(ShutdownTimeout));
	});

	/// <summary>
	/// Cancels every transfer and closes every session. Completes once the canceled transfers have stopped, which is
	/// after a partial download has been deleted.
	/// </summary>
	private async Task StopAllAsync()
	{
		Registration[] registrations;
		lock (_lock)
		{
			registrations = [.. _registrations];
		}

		foreach (Registration registration in registrations)
		{
			foreach (ITransferItem item in registration.Transfers.Items)
			{
				if (item.State is TransferState.Queued or TransferState.Running)
				{
					registration.Transfers.Cancel(item.Id);
				}
			}
		}

		await Task.WhenAll(registrations.Select(registration => registration.Sessions.CloseAllAsync()));

		// A canceled transfer keeps its Running state until its work has stopped and cleaned up.
		while (registrations.Any(registration => registration.Transfers.Items.Any(item => item.State == TransferState.Running)))
		{
			await Task.Delay(PollInterval);
		}
	}

	private static async Task Report(Func<Task> work)
	{
		try
		{
			await work();
		}
		catch (Exception ex)
		{
			// Release builds register no logging provider, so the crash log is the only place this can be read later.
			CrashLog.Write("Shutdown", ex, terminating: true);
		}
	}

	private void Forget(Registration registration)
	{
		lock (_lock)
		{
			_ = _registrations.Remove(registration);
		}
	}

	private sealed class Registration : IDisposable
	{
		private readonly DesktopSessions _owner;

		public Registration(DesktopSessions owner, ISessionManager sessions, ITransferQueue transfers)
		{
			_owner = owner;
			Sessions = sessions;
			Transfers = transfers;
		}

		public ISessionManager Sessions { get; }

		public ITransferQueue Transfers { get; }

		public void Dispose() => _owner.Forget(this);
	}
}
