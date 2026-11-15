namespace Mokaterm.UI.Common.Platform;

/// <summary>Whether system notifications may be shown, where the user decides that (a browser).</summary>
public enum SystemNotificationConsent
{
	/// <summary>The host cannot show them at all.</summary>
	Unsupported,

	/// <summary>The user has not been asked yet.</summary>
	NotAsked,

	Granted,

	/// <summary>The user refused; only they can change it, in the browser's site settings.</summary>
	Denied,
}
