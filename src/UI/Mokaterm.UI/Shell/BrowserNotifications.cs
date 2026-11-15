using Microsoft.Extensions.Logging;
using Moka.Red.Feedback.SystemNotification;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.UI.Common.Interop;
using Mokaterm.UI.Common.Platform;

namespace Mokaterm.UI.Shell;

/// <summary>
/// <see cref="ISystemNotifications"/> on the web hosts: the browser's own notifications through Moka.Red's
/// <see cref="IMokaSystemNotifier"/>, so a trigger still reaches someone working in another tab. One per circuit. The
/// browser is reached from the renderer only, through the dispatch the shell attaches while it is on screen; before that
/// and after it nothing is shown.
/// </summary>
internal sealed class BrowserNotifications : ISystemNotifications, ISystemNotificationConsent
{
	private readonly IMokaSystemNotifier _notifier;
	private readonly PageVisibility _visibility;
	private readonly ISessionManager _sessions;
	private readonly ISessionTabs _tabs;
	private readonly ILogger _logger;
	private readonly Lock _gate = new();
	private Func<Func<Task>, Task>? _dispatch;

	// What the browser last said, so a refusal costs no call into the page for every trigger; unknown until it says.
	private SystemNotificationConsent? _consent;

	public BrowserNotifications(
		IMokaSystemNotifier notifier,
		PageVisibility visibility,
		ISessionManager sessions,
		ISessionTabs tabs,
		ILogger<BrowserNotifications> logger)
	{
		_notifier = notifier;
		_visibility = visibility;
		_sessions = sessions;
		_tabs = tabs;
		_logger = logger;
	}

	public bool IsSessionWindowInForeground => _visibility.IsVisible;

	/// <summary>Lets notifications through <paramref name="dispatch"/>, the shell's <c>InvokeAsync</c>, until disposed.</summary>
	public IDisposable Attach(Func<Func<Task>, Task> dispatch)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		lock (_gate)
		{
			_dispatch = dispatch;
		}

		return new Attachment(this, dispatch);
	}

	public bool Show(SystemNotification notification)
	{
		ArgumentNullException.ThrowIfNull(notification);
		Func<Func<Task>, Task>? dispatch;
		SystemNotificationConsent? consent;
		lock (_gate)
		{
			dispatch = _dispatch;
			consent = _consent;
		}

		if (dispatch is null || consent is SystemNotificationConsent.Denied or SystemNotificationConsent.Unsupported)
		{
			return false;
		}

		_ = ShowAsync(dispatch, notification);
		return true;
	}

	public async ValueTask<SystemNotificationConsent> GetAsync(CancellationToken cancellationToken = default) =>
		Remember(await _notifier.GetPermissionAsync(cancellationToken));

	public async ValueTask<SystemNotificationConsent> RequestAsync(CancellationToken cancellationToken = default) =>
		Remember(await _notifier.RequestPermissionAsync(cancellationToken));

	private async Task ShowAsync(Func<Func<Task>, Task> dispatch, SystemNotification notification)
	{
		try
		{
			await dispatch(async () =>
			{
				MokaSystemNotificationResult result = await _notifier.NotifyAsync(new MokaSystemNotification
				{
					Title = notification.Title,
					Body = notification.Detail is { } detail ? $"{notification.Message}\n{detail}" : notification.Message,
					OnClick = () => SelectSession(notification),
				});
				if (result is MokaSystemNotificationResult.NotAllowed)
				{
					_ = Remember(MokaSystemNotificationConsent.NotAsked);
				}
				else if (result is MokaSystemNotificationResult.Unsupported)
				{
					_ = Remember(MokaSystemNotificationConsent.Unsupported);
				}
			});
		}
		catch (Exception ex) when (JsModule.IsExpected(ex))
		{
			_logger.LogDebug("A browser notification could not be shown ({Failure}).", LogSafe.Describe(ex));
		}
	}

	private Task SelectSession(SystemNotification notification)
	{
		if (SystemNotification.SessionToShow(notification.SessionId, notification.ConnectionId, _sessions.Sessions) is { } session)
		{
			_tabs.Show(session);
		}

		return Task.CompletedTask;
	}

	private SystemNotificationConsent Remember(MokaSystemNotificationConsent consent)
	{
		SystemNotificationConsent ours = consent switch
		{
			MokaSystemNotificationConsent.Granted => SystemNotificationConsent.Granted,
			MokaSystemNotificationConsent.Denied => SystemNotificationConsent.Denied,
			MokaSystemNotificationConsent.NotAsked => SystemNotificationConsent.NotAsked,
			_ => SystemNotificationConsent.Unsupported,
		};
		lock (_gate)
		{
			_consent = ours;
		}

		return ours;
	}

	private void Detach(Func<Func<Task>, Task> dispatch)
	{
		lock (_gate)
		{
			// Only the dispatch that attachment set is taken away, so a shell that came back first keeps its own.
			if (ReferenceEquals(_dispatch, dispatch))
			{
				_dispatch = null;
			}
		}
	}

	private sealed class Attachment(BrowserNotifications owner, Func<Func<Task>, Task> dispatch) : IDisposable
	{
		public void Dispose() => owner.Detach(dispatch);
	}
}
