namespace Mokaterm.Abstractions.Platform;

/// <summary>Where the app stands with updates.</summary>
public enum UpdateStage
{
	/// <summary>Nothing has been checked in this run.</summary>
	Unknown,

	/// <summary>No feed is set, so nothing is ever checked.</summary>
	NotConfigured,

	/// <summary>
	/// This copy cannot replace its own files, because it was never installed. A build straight out of <c>bin</c> and an
	/// unpacked copy both look like this.
	/// </summary>
	NotInstalled,

	Checking,

	/// <summary>The feed has nothing newer than the running version.</summary>
	UpToDate,

	/// <summary>A newer version is in the feed and nothing has been downloaded yet.</summary>
	Available,

	Downloading,

	/// <summary>The new version is on disk. Restarting is all that is left.</summary>
	ReadyToRestart,

	/// <summary>The last check, download or restart failed; <see cref="AppUpdateState.Message"/> says what to make of it.</summary>
	Failed,
}

/// <summary>Everything the update UI shows. Immutable: each change arrives as a new value on <see cref="IAppUpdater.StateChanged"/>.</summary>
public sealed record AppUpdateState
{
	public required UpdateStage Stage { get; init; }

	/// <summary>The version the feed offers, once a check or a finished download named one.</summary>
	public string? AvailableVersion { get; init; }

	/// <summary>0 to 100 while <see cref="Stage"/> is <see cref="UpdateStage.Downloading"/>.</summary>
	public int DownloadPercent { get; init; }

	/// <summary>A sentence for the user: why updates are off, why this copy cannot update, or what failed.</summary>
	public string? Message { get; init; }

	/// <summary>When the last check finished, whether it found anything or failed.</summary>
	public DateTimeOffset? LastCheck { get; init; }
}

/// <summary>
/// In-app updates, registered only by a host that can replace its own files: the desktop app does, the web host never
/// will. The UI asks for it with <c>GetService</c> and leaves the feature out when it is missing.
/// Nothing here downloads or restarts on its own. A restart drops every live terminal, so the user starts it.
/// Every method reports a failure as <see cref="UpdateStage.Failed"/> in the returned state rather than throwing.
/// </summary>
public interface IAppUpdater
{
	/// <summary>The latest state, and the starting point for a page that has just opened.</summary>
	AppUpdateState State { get; }

	/// <summary>Raised for every change, download progress included. Handlers may run on any thread.</summary>
	event Action<AppUpdateState>? StateChanged;

	/// <summary>Reads the feed and works out whether a newer version is there.</summary>
	Task<AppUpdateState> CheckAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Downloads the version the last check found, reporting progress through <see cref="StateChanged"/>. Returns the
	/// current state unchanged unless <see cref="State"/> is <see cref="UpdateStage.Available"/>.
	/// </summary>
	Task<AppUpdateState> DownloadAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Restarts into the downloaded version, so a call that works never returns. Anything the host has to finish first,
	/// such as closing sessions, happens in here. Returns the current state unchanged unless it is
	/// <see cref="UpdateStage.ReadyToRestart"/>.
	/// </summary>
	Task<AppUpdateState> ApplyAndRestartAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// The quiet check the host runs at startup, at most once per app start, and only while a feed is set and
	/// <see cref="Settings.UpdateSettings.CheckAtStartup"/> is on. Null when no check ran, so the caller only has to
	/// decide whether to say anything about the state it gets back.
	/// </summary>
	Task<AppUpdateState?> CheckAtStartupAsync(CancellationToken cancellationToken = default);
}
