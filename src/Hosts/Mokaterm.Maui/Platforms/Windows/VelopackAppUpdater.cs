using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Maui.Services;
using Velopack;
using Velopack.Locators;

namespace Mokaterm.Maui.Platforms.Windows;

/// <summary>
/// In-app updates through Velopack, against the feed in <see cref="UpdateSettings"/>: an https address or a folder,
/// which <see cref="UpdateManager"/> tells apart itself. One instance for the whole app, because a downloaded package
/// belongs to the install and not to a window.
/// <para>
/// Velopack only works inside an install: <c>VelopackApp.Build().Run()</c> in the entry point leaves the locator that
/// finds it behind, and every call throws where there is none. A build run out of <c>bin</c>, and a copied install
/// folder, therefore rest at <see cref="UpdateStage.NotInstalled"/> instead of failing on each attempt.
/// </para>
/// </summary>
internal sealed class VelopackAppUpdater : IAppUpdater
{
	private readonly Lock _gate = new();
	private readonly ISettingsService _settings;
	private readonly DesktopSessions _sessions;
	private readonly TimeProvider _time;
	private readonly ILogger<VelopackAppUpdater> _logger;

	private AppUpdateState _state;
	private UpdateInfo? _found;
	private int _startupChecked;

	public VelopackAppUpdater(
		ISettingsService settings,
		DesktopSessions sessions,
		TimeProvider time,
		ILogger<VelopackAppUpdater> logger)
	{
		_settings = settings;
		_sessions = sessions;
		_time = time;
		_logger = logger;
		_state = Resting();
		_settings.Changed += OnSettingsChanged;
	}

	public event Action<AppUpdateState>? StateChanged;

	public AppUpdateState State
	{
		get
		{
			lock (_gate)
			{
				return _state;
			}
		}
	}

	public async Task<AppUpdateState> CheckAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			if (TryOpen() is not { } manager)
			{
				return Publish(Resting());
			}

			Publish(State with { Stage = UpdateStage.Checking, Message = null, DownloadPercent = 0 });

			// CheckForUpdatesAsync takes no token and starts its work on the calling thread, so it goes to the pool and
			// the token only keeps a queued check from starting at all.
			UpdateInfo? found = await Task.Run(() => manager.CheckForUpdatesAsync(), cancellationToken).ConfigureAwait(false);
			lock (_gate)
			{
				_found = found;
			}

			if (found is null)
			{
				return Publish(new AppUpdateState { Stage = UpdateStage.UpToDate, LastCheck = _time.GetUtcNow() });
			}

			// The feed answers against the installed version, so it offers what is already downloaded again; saying so
			// would send the user through a download that has nothing left to fetch.
			string? offered = Describe(found.TargetFullRelease);
			bool downloaded = offered is not null && Describe(manager.UpdatePendingRestart) == offered;
			return Publish(new AppUpdateState
			{
				Stage = downloaded ? UpdateStage.ReadyToRestart : UpdateStage.Available,
				AvailableVersion = offered,
				DownloadPercent = downloaded ? 100 : 0,
				LastCheck = _time.GetUtcNow(),
			});
		}
		catch (Exception ex)
		{
			_logger.LogWarning("Checking for updates failed: {Failure}", LogSafe.Describe(ex));
			return Publish(new AppUpdateState
			{
				Stage = UpdateStage.Failed,
				Message = "The update feed could not be read. Check the feed location and the network.",
				LastCheck = _time.GetUtcNow(),
			});
		}
	}

	public async Task<AppUpdateState> DownloadAsync(CancellationToken cancellationToken = default)
	{
		UpdateInfo? found;
		lock (_gate)
		{
			if (_state.Stage != UpdateStage.Available)
			{
				return _state;
			}

			found = _found;
		}

		try
		{
			if (found is null || TryOpen() is not { } manager)
			{
				return Publish(Resting());
			}

			Publish(State with { Stage = UpdateStage.Downloading, Message = null, DownloadPercent = 0 });
			await Task.Run(
				() => manager.DownloadUpdatesAsync(found, OnDownloadProgress, cancellationToken),
				cancellationToken).ConfigureAwait(false);

			return Publish(State with { Stage = UpdateStage.ReadyToRestart, Message = null, DownloadPercent = 100 });
		}
		catch (OperationCanceledException)
		{
			return Publish(Resting());
		}
		catch (Exception ex)
		{
			_logger.LogWarning("Downloading an update failed: {Failure}", LogSafe.Describe(ex));
			return Publish(State with
			{
				Stage = UpdateStage.Failed,
				Message = "The new version could not be downloaded. Nothing on this install changed.",
				DownloadPercent = 0,
			});
		}
	}

	public async Task<AppUpdateState> ApplyAndRestartAsync(CancellationToken cancellationToken = default)
	{
		lock (_gate)
		{
			if (_state.Stage != UpdateStage.ReadyToRestart)
			{
				return _state;
			}
		}

		try
		{
			if (TryOpen() is not { } manager)
			{
				return Publish(Resting());
			}

			// Velopack names the package it holds on disk, which is also what a start between the download and this
			// restart leaves behind.
			if (manager.UpdatePendingRestart is not { } ready)
			{
				return Publish(State with
				{
					Stage = UpdateStage.Failed,
					Message = "The downloaded version is no longer on disk. Check for updates again.",
				});
			}

			// The restart ends this process outright and MAUI's close handler never runs for it, so the shutdown the
			// window would have done, deadline included, happens here instead.
			await _sessions.ShutDownAsync().ConfigureAwait(false);

			manager.ApplyUpdatesAndRestart(ready);
			return State;
		}
		catch (Exception ex)
		{
			_logger.LogWarning("Applying an update failed: {Failure}", LogSafe.Describe(ex));
			return Publish(State with
			{
				Stage = UpdateStage.Failed,
				Message = "The update could not be installed. This version keeps running.",
			});
		}
	}

	public async Task<AppUpdateState?> CheckAtStartupAsync(CancellationToken cancellationToken = default)
	{
		UpdateSettings settings = _settings.Get<UpdateSettings>();
		if (!settings.CheckAtStartup || UpdateSettings.NormalizeFeed(settings.Feed).Length == 0)
		{
			return null;
		}

		// Once per app start: every window shares this service, and a page that is built again must not check twice.
		if (Interlocked.Exchange(ref _startupChecked, 1) != 0)
		{
			return null;
		}

		return await CheckAsync(cancellationToken).ConfigureAwait(false);
	}

	private static string? Describe(VelopackAsset? asset) => asset?.Version?.ToString();

	/// <summary>A manager for the feed in settings, or null with no feed or where this copy cannot replace its own files.</summary>
	private UpdateManager? TryOpen()
	{
		string feed = Feed();

		// The constructor needs the locator VelopackApp.Build().Run() sets up, and throws rather than guess without it.
		if (feed.Length == 0 || !VelopackLocator.IsCurrentSet)
		{
			return null;
		}

		UpdateManager manager = new(feed);
		return manager.IsInstalled ? manager : null;
	}

	private string Feed() => UpdateSettings.NormalizeFeed(_settings.Get<UpdateSettings>().Feed);

	/// <summary>
	/// The state of an install nothing has been asked of yet, and the one an abandoned attempt falls back to. Never
	/// throws: this is what the constructor starts from, and the app has to open whatever Velopack makes of its folders.
	/// </summary>
	private AppUpdateState Resting()
	{
		try
		{
			if (Feed().Length == 0)
			{
				return new AppUpdateState
				{
					Stage = UpdateStage.NotConfigured,
					Message = "No update feed is set, so Mokaterm never looks for a new version.",
				};
			}

			if (TryOpen() is not { } manager)
			{
				return new AppUpdateState
				{
					Stage = UpdateStage.NotInstalled,
					Message = "This copy of Mokaterm was not installed, so it cannot replace its own files. "
						+ "Install it to update it in place.",
				};
			}

			// A package downloaded in an earlier run is still there, so restarting is all that is left to do.
			return manager.UpdatePendingRestart is { } ready
				? new AppUpdateState { Stage = UpdateStage.ReadyToRestart, AvailableVersion = Describe(ready), DownloadPercent = 100 }
				: new AppUpdateState { Stage = UpdateStage.Unknown };
		}
		catch (Exception ex)
		{
			_logger.LogWarning("Reading the update state failed: {Failure}", LogSafe.Describe(ex));
			return new AppUpdateState
			{
				Stage = UpdateStage.Failed,
				Message = "The state of this install could not be read, so updates are unavailable.",
			};
		}
	}

	private void OnSettingsChanged(string sectionKey)
	{
		if (sectionKey != UpdateSettings.SectionKey)
		{
			return;
		}

		// The feed decides whether updates are possible at all, so editing it shows at once. A check or a download under
		// way keeps the state it is in.
		lock (_gate)
		{
			if (_state.Stage is not (UpdateStage.Unknown or UpdateStage.NotConfigured or UpdateStage.NotInstalled))
			{
				return;
			}
		}

		Publish(Resting());
	}

	private void OnDownloadProgress(int percent)
	{
		int reached = Math.Clamp(percent, 0, 100);
		Publish(state => state.Stage == UpdateStage.Downloading && state.DownloadPercent != reached
			? state with { DownloadPercent = reached }
			: null);
	}

	private AppUpdateState Publish(AppUpdateState state) => Publish(_ => state);

	/// <summary>
	/// Replaces the state with what <paramref name="next"/> makes of the current one, then tells listeners outside the
	/// lock. A null result, or one equal to the state already there, changes nothing and raises nothing.
	/// </summary>
	private AppUpdateState Publish(Func<AppUpdateState, AppUpdateState?> next)
	{
		AppUpdateState published;
		lock (_gate)
		{
			if (next(_state) is not { } state || _state == state)
			{
				return _state;
			}

			_state = state;
			published = state;
		}

		StateChanged?.Invoke(published);
		return published;
	}
}
