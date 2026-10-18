using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Platform;

namespace Mokaterm.UI.Terminal.Triggers;

/// <summary>
/// The system notification that goes with a trigger's notice while the window holding the sessions is minimized or
/// behind another one, where the toast alone would expire unseen. Only where the host has <see cref="ISystemNotifications"/>
/// (the desktop) and the setting is on. Window state, scoped like the sessions, so each session's limit survives a tab
/// moving to another pane and getting a new view.
/// </summary>
internal sealed class TriggerSystemNotices
{
	private static readonly TimeSpan Interval = TimeSpan.FromSeconds(TerminalTriggerSettings.NotifyIntervalSeconds);

	private readonly ISettingsService _settings;
	private readonly TimeProvider _time;
	private readonly ISystemNotifications? _system;
	private readonly ILogger _logger;
	private readonly Lock _lock = new();
	private readonly Dictionary<(Guid Session, Guid Rule), long> _shown = [];

	public TriggerSystemNotices(
		ISettingsService settings,
		TimeProvider timeProvider,
		ILogger<TriggerSystemNotices> logger,
		ISystemNotifications? systemNotifications = null)
	{
		_settings = settings;
		_time = timeProvider;
		_logger = logger;
		_system = systemNotifications;
	}

	/// <summary>
	/// A rule with a notice matched <paramref name="line"/> in <paramref name="source"/>, and its toast is on its way.
	/// Shows the system notification as well when nobody is looking at the window, at most once per rule and session per
	/// <see cref="TerminalTriggerSettings.NotifyIntervalSeconds"/>, the limit the toast already has. True when one was shown.
	/// </summary>
	public bool Raise(TriggerSource source, TerminalTrigger rule, string? line)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(rule);
		if (_system is null || !rule.Notify)
		{
			return false;
		}

		TerminalTriggerSettings settings = _settings.Get<TerminalTriggerSettings>();
		if (!settings.SystemNotifications)
		{
			return false;
		}

		try
		{
			// Taken only once the window is known to be out of sight, and before showing, so a show that fails is not
			// retried on every line of a looping error.
			if (_system.IsSessionWindowInForeground || !TryTake(source.SessionId, rule.Id))
			{
				return false;
			}

			return _system.Show(TriggerNotice.ForSystem(source, rule, line, settings.SystemNotificationShowsLine));
		}
		catch (Exception ex)
		{
			// The contract says it never throws; a host that breaks it must not take the terminal down with it.
			_logger.LogWarning("A trigger's system notification failed ({Failure}).", LogSafe.Describe(ex));
			return false;
		}
	}

	private bool TryTake(Guid session, Guid rule)
	{
		long now = _time.GetTimestamp();
		lock (_lock)
		{
			if (_shown.TryGetValue((session, rule), out long last) && _time.GetElapsedTime(last, now) < Interval)
			{
				return false;
			}

			// Old entries go as new ones come, so closed sessions and deleted rules do not pile up.
			foreach ((Guid, Guid) stale in _shown.Where(entry => _time.GetElapsedTime(entry.Value, now) >= Interval).Select(entry => entry.Key).ToList())
			{
				_ = _shown.Remove(stale);
			}

			_shown[(session, rule)] = now;
			return true;
		}
	}
}
