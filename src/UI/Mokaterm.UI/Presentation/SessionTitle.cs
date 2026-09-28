using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Sessions;

namespace Mokaterm.UI.Presentation;

/// <summary>
/// Splits what a session is called into the two parts a tab shows side by side: the name someone gave it and the
/// endpoint it stands for. <see cref="ConnectionProfile.GetTitle"/> folds them into one string, which loses whichever
/// part it did not pick.
/// </summary>
internal static class SessionTitle
{
	/// <summary>The login's label, or the machine's name. Null when neither says more than the address.</summary>
	public static string? Name(ISessionHandle session)
	{
		ArgumentNullException.ThrowIfNull(session);
		return Name(session.Host, session.Connection);
	}

	public static string? Name(HostProfile host, ConnectionProfile connection)
	{
		ArgumentNullException.ThrowIfNull(host);
		ArgumentNullException.ThrowIfNull(connection);
		if (!string.IsNullOrWhiteSpace(connection.Label))
		{
			return connection.Label.Trim();
		}

		string name = host.Name?.Trim() ?? "";
		return name.Length > 0 && !string.Equals(name, host.Address, StringComparison.OrdinalIgnoreCase) ? name : null;
	}

	/// <summary><c>user@host</c>, or the address alone for a login without a user. The port stays for the tooltip.</summary>
	public static string Endpoint(ISessionHandle session)
	{
		ArgumentNullException.ThrowIfNull(session);
		return Endpoint(session.Host, session.Connection);
	}

	public static string Endpoint(HostProfile host, ConnectionProfile connection)
	{
		ArgumentNullException.ThrowIfNull(host);
		ArgumentNullException.ThrowIfNull(connection);
		return string.IsNullOrWhiteSpace(connection.Username) ? host.Address : $"{connection.Username}@{host.Address}";
	}
}
