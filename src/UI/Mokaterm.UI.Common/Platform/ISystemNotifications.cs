namespace Mokaterm.UI.Common.Platform;

/// <summary>
/// Notifications the operating system shows outside the app, for something a session said while the user looked
/// elsewhere: a toast in the page expires unseen behind another window. Only the desktop host registers one; the web host
/// and the DevHost keep their toasts and nothing else.
/// </summary>
/// <remarks>
/// A click on one brings the window holding the sessions to the front and selects the session named in it. The host
/// does that, from <see cref="SystemNotification.SessionId"/> and <see cref="SystemNotification.ConnectionId"/>, which
/// are all the notification carries back.
/// </remarks>
public interface ISystemNotifications
{
	/// <summary>
	/// Whether the window that holds the session tabs is the foreground window and is not minimized, which is when a toast
	/// in it is seen and a system notification would only repeat it. Any thread; never throws.
	/// </summary>
	bool IsSessionWindowInForeground { get; }

	/// <summary>
	/// Shows <paramref name="notification"/>. False when it could not be: the app could not register for notifications,
	/// the system refused it, or the user turned the app's notifications off. Any thread; never throws.
	/// </summary>
	bool Show(SystemNotification notification);
}
