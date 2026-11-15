using Moka.Red.Feedback.Notification;
using Moka.Red.Feedback.Toast;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.UI.Interaction;

namespace Mokaterm.UI.Tests;

/// <summary>
/// The bell keeps the warnings and errors a toast showed, newest first, and never more than its cap, because Moka's
/// center keeps whatever it is given for as long as the window lives.
/// </summary>
public sealed class NoticeHistoryTests
{
	[Theory]
	[InlineData(NoticeSeverity.Warning, true)]
	[InlineData(NoticeSeverity.Error, true)]
	[InlineData(NoticeSeverity.Info, false)]
	[InlineData(NoticeSeverity.Success, false)]
	public void OnlyWarningsAndErrors_AreKept(NoticeSeverity severity, bool kept) =>
		Assert.Equal(kept, new PendingNotice(severity, "message", null).IsKept);

	[Fact]
	public void AKeptNotice_TakesItsTitleAndSeverity_OrOneByItsSeverity()
	{
		MokaNotificationService notifications = new();

		NoticeHistory.Keep(notifications, new PendingNotice(NoticeSeverity.Warning, "restarted 4 times", "Docker on build"));
		NoticeHistory.Keep(notifications, new PendingNotice(NoticeSeverity.Error, "could not be saved", null));

		Assert.Collection(
			notifications.Notifications,
			newest =>
			{
				Assert.Equal("Error", newest.Title);
				Assert.Equal(MokaToastSeverity.Error, newest.Severity);
			},
			oldest =>
			{
				Assert.Equal("Docker on build", oldest.Title);
				Assert.Equal("restarted 4 times", oldest.Message);
				Assert.Equal(MokaToastSeverity.Warning, oldest.Severity);
			});
	}

	[Fact]
	public void PastTheCap_TheOldestGo()
	{
		MokaNotificationService notifications = new();

		for (int index = 0; index <= NoticeHistory.MaxKept; index++)
		{
			NoticeHistory.Keep(notifications, new PendingNotice(NoticeSeverity.Warning, $"notice {index}", "Docker"));
		}

		Assert.Equal(NoticeHistory.MaxKept, notifications.Notifications.Count);
		Assert.Equal($"notice {NoticeHistory.MaxKept}", notifications.Notifications[0].Message);
		Assert.Equal("notice 1", notifications.Notifications[^1].Message);
	}

	[Fact]
	public void TheRelay_HandsANoticeToWhoeverListens()
	{
		NoticeRelay relay = new();
		List<PendingNotice> heard = [];
		relay.Kept += heard.Add;
		PendingNotice notice = new(NoticeSeverity.Error, "failed", "Docker");

		relay.Keep(notice);

		Assert.Equal([notice], heard);
	}
}
