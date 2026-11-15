namespace Mokaterm.UI.Common.Platform;

/// <summary>
/// The user's say over <see cref="ISystemNotifications"/>, registered beside it by a host whose notifications need a
/// permission the page asks for: the browser. The desktop's are the app's own and need none, so it registers no consent.
/// </summary>
public interface ISystemNotificationConsent
{
	/// <summary>Where the permission stands. Call it from an interactive render.</summary>
	ValueTask<SystemNotificationConsent> GetAsync(CancellationToken cancellationToken = default);

	/// <summary>Asks the user, which a browser allows only straight from something they pressed.</summary>
	ValueTask<SystemNotificationConsent> RequestAsync(CancellationToken cancellationToken = default);
}
