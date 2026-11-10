using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.UI.Presentation;

namespace Mokaterm.UI.Shell;

// What the window title says about the active session: the parts its tab shows, the login's name and user@host, and
// nothing the tab does not. What the remote shell calls itself stays out, because a server can write anything there.
internal static class WindowTitleText
{
	public static string? Subject(ISessionHandle? session) => session is null ? null : Subject(session.Host, session.Connection);

	public static string Subject(HostProfile host, ConnectionProfile connection)
	{
		string endpoint = SessionTitle.Endpoint(host, connection);
		return SessionTitle.Name(host, connection) is { } name ? $"{name} ({endpoint})" : endpoint;
	}
}
