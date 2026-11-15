using Moka.Red.Feedback.Notification;

namespace Mokaterm.UI.Interaction;

/// <summary>What the top bar's bell keeps of the notices every window of the group showed as toasts.</summary>
internal static class NoticeHistory
{
	// Moka's center keeps whatever it is given, so the oldest go once this many are kept.
	public const int MaxKept = 100;

	/// <summary>Puts <paramref name="notice"/> at the top of <paramref name="notifications"/> and lets the oldest past the cap go.</summary>
	public static void Keep(IMokaNotificationService notifications, PendingNotice notice)
	{
		ArgumentNullException.ThrowIfNull(notifications);
		ArgumentNullException.ThrowIfNull(notice);
		notifications.Push(notice.KeptTitle, notice.Message, notice.ToastSeverity);
		IReadOnlyList<MokaNotification> kept = notifications.Notifications;
		for (int index = kept.Count - 1; index >= MaxKept; index--)
		{
			notifications.Remove(kept[index].Id);
		}
	}
}
