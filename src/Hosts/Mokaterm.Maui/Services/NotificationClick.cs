using System.Globalization;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.UI.Common.Platform;

namespace Mokaterm.Maui.Services;

/// <summary>
/// What a click on a system notification comes back with: ids and nothing else. They travel as the notification's
/// arguments, which the system keeps in a store of its own after the app has gone, so no machine or user name is ever
/// among them. Plain data with no platform call, so the tests can check it without Windows.
/// </summary>
/// <param name="SessionId">The session the notification was about.</param>
/// <param name="ConnectionId">The saved login behind it, for a session that was closed and opened again since.</param>
internal sealed record NotificationClick(Guid? SessionId, Guid? ConnectionId)
{
	public const string SessionKey = "session";

	public const string ConnectionKey = "connection";

	// Hex digits only, so a value needs no escaping in the notification's argument string.
	private const string IdFormat = "N";

	/// <summary>The arguments a notification for <paramref name="notification"/> carries.</summary>
	public static IReadOnlyList<KeyValuePair<string, string>> ArgumentsFor(SystemNotification notification)
	{
		ArgumentNullException.ThrowIfNull(notification);
		List<KeyValuePair<string, string>> arguments = [];
		if (notification.SessionId is { } session)
		{
			arguments.Add(new(SessionKey, session.ToString(IdFormat, CultureInfo.InvariantCulture)));
		}

		if (notification.ConnectionId is { } connection)
		{
			arguments.Add(new(ConnectionKey, connection.ToString(IdFormat, CultureInfo.InvariantCulture)));
		}

		return arguments;
	}

	/// <summary>
	/// Reads a click's arguments back. Anything but our two ids in our own form is passed over: any program can start the
	/// app with arguments of its choosing.
	/// </summary>
	public static NotificationClick Read(IEnumerable<KeyValuePair<string, string>>? arguments)
	{
		Guid? session = null;
		Guid? connection = null;
		foreach ((string key, string value) in arguments ?? [])
		{
			if (!Guid.TryParseExact(value, IdFormat, out Guid id))
			{
				continue;
			}

			if (string.Equals(key, SessionKey, StringComparison.Ordinal))
			{
				session = id;
			}
			else if (string.Equals(key, ConnectionKey, StringComparison.Ordinal))
			{
				connection = id;
			}
		}

		return new NotificationClick(session, connection);
	}

	/// <summary>
	/// The session the click selects: the one the notification was about while it is open, else an open session of the
	/// same saved login, else none, and the window only comes to the front.
	/// </summary>
	public Guid? SessionToShow(IEnumerable<ISessionHandle> sessions)
	{
		ArgumentNullException.ThrowIfNull(sessions);
		ISessionHandle[] open = [.. sessions];
		if (SessionId is { } id && open.Any(session => session.Id == id))
		{
			return id;
		}

		return ConnectionId is { } connection
			? open.FirstOrDefault(session => !session.IsTransient && session.Connection.Id == connection)?.Id
			: null;
	}
}
