using Microsoft.Extensions.Logging.Abstractions;
using Moka.Red.Feedback.SystemNotification;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.UI.Common.Platform;
using Mokaterm.UI.Shell;
using Mokaterm.UI.Tests.Fakes;

namespace Mokaterm.UI.Tests;

/// <summary>
/// The web hosts' system notifications: the browser's own, reached only through the shell's renderer, quiet once the
/// browser said no, and a click selects the session the notification was about.
/// </summary>
public sealed class BrowserNotificationsTests
{
	private static readonly SystemNotification Notice = new() { Title = "Trigger in build", Message = "Matched \"ERROR\"" };

	private readonly FakeNotifier _notifier = new();
	private readonly PageVisibility _visibility = new();
	private readonly ManualSessionManager _sessions = new();
	private readonly RecordingTabs _tabs = new();

	[Fact]
	public void BeforeTheShellAttaches_NothingIsShown()
	{
		BrowserNotifications notifications = Create();

		Assert.False(notifications.Show(Notice));
		Assert.Empty(_notifier.Shown);
	}

	[Fact]
	public void OnceAttached_ANoticeGoesThroughTheShellsDispatch()
	{
		BrowserNotifications notifications = Create();
		int dispatched = 0;
		using IDisposable attachment = notifications.Attach(work =>
		{
			dispatched++;
			return work();
		});

		Assert.True(notifications.Show(Notice with { Detail = "build failed: ERROR 42" }));

		Assert.Equal(1, dispatched);
		MokaSystemNotification shown = Assert.Single(_notifier.Shown);
		Assert.Equal("Trigger in build", shown.Title);
		Assert.Equal("Matched \"ERROR\"\nbuild failed: ERROR 42", shown.Body);
	}

	[Fact]
	public async Task OnceTheBrowserRefused_ItIsNotAskedAgainForEveryTrigger()
	{
		BrowserNotifications notifications = Create();
		using IDisposable attachment = notifications.Attach(work => work());
		_notifier.Consent = MokaSystemNotificationConsent.Denied;

		Assert.Equal(SystemNotificationConsent.Denied, await notifications.GetAsync(TestContext.Current.CancellationToken));

		Assert.False(notifications.Show(Notice));
		Assert.Empty(_notifier.Shown);
	}

	[Fact]
	public void AfterTheShellLeft_NothingIsShown()
	{
		BrowserNotifications notifications = Create();
		IDisposable attachment = notifications.Attach(work => work());

		attachment.Dispose();

		Assert.False(notifications.Show(Notice));
	}

	[Fact]
	public async Task AClick_SelectsTheSessionItWasAbout()
	{
		BrowserNotifications notifications = Create();
		using IDisposable attachment = notifications.Attach(work => work());
		FakeSession session = _sessions.Add();

		_ = notifications.Show(Notice with { SessionId = session.Id });
		await Assert.Single(_notifier.Shown).OnClick!();

		Assert.Equal([session.Id], _tabs.Shown);
	}

	[Fact]
	public void TheWindowIsInFront_WhileThePageCanBeSeen()
	{
		BrowserNotifications notifications = Create();

		_visibility.Set(false);

		Assert.False(notifications.IsSessionWindowInForeground);
	}

	private BrowserNotifications Create() =>
		new(_notifier, _visibility, _sessions, _tabs, NullLogger<BrowserNotifications>.Instance);

	private sealed class FakeNotifier : IMokaSystemNotifier
	{
		public List<MokaSystemNotification> Shown { get; } = [];

		public MokaSystemNotificationConsent Consent { get; set; } = MokaSystemNotificationConsent.Granted;

		public ValueTask<MokaSystemNotificationConsent> GetPermissionAsync(CancellationToken cancellationToken = default) =>
			ValueTask.FromResult(Consent);

		public ValueTask<MokaSystemNotificationConsent> RequestPermissionAsync(CancellationToken cancellationToken = default) =>
			ValueTask.FromResult(Consent);

		public ValueTask<MokaSystemNotificationResult> NotifyAsync(MokaSystemNotification notification, CancellationToken cancellationToken = default)
		{
			Shown.Add(notification);
			return ValueTask.FromResult(MokaSystemNotificationResult.Shown);
		}
	}

	private sealed class RecordingTabs : ISessionTabs
	{
		public List<Guid> Shown { get; } = [];

		public void Show(Guid sessionId) => Shown.Add(sessionId);
	}
}
