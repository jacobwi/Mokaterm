using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Sessions;

namespace Mokaterm.UI.Presentation;

/// <summary>Formats <c>user@host:port</c>. The host and the port are <see cref="HostEndpoint"/>'s rule; the user is this one's.</summary>
internal static class EndpointFormat
{
	public static string Format(string? username, string address, int port)
	{
		string endpoint = HostEndpoint.Format(address, port);
		return string.IsNullOrWhiteSpace(username) ? endpoint : $"{username}@{endpoint}";
	}

	/// <summary>
	/// The endpoint a session talks to, with the protocol default port filled in. A protocol without a port (a serial
	/// line, whose address is the port name) is written without one.
	/// </summary>
	public static string Format(ISessionHandle session) =>
		session.Protocol.UsesPort
			? Format(session.Connection.Username, session.Host.Address, session.Connection.Port ?? session.Protocol.DefaultPort)
			: Format(session.Connection.Username, session.Host.Address);

	private static string Format(string? username, string address) =>
		string.IsNullOrWhiteSpace(username) ? address : $"{username}@{address}";
}
