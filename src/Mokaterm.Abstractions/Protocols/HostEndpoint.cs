using System.Globalization;

namespace Mokaterm.Abstractions.Protocols;

/// <summary>
/// The one <c>host:port</c> rule, for messages and for anything else that names a server. An IPv6 address goes in
/// brackets because its own colons would otherwise swallow the port: <c>::1:5900</c> is a valid address on its own and
/// says nothing about which part was meant to be the port.
/// </summary>
public static class HostEndpoint
{
	/// <summary><c>host:port</c>, bracketing an IPv6 address unless it already carries brackets.</summary>
	public static string Format(string host, int port)
	{
		ArgumentNullException.ThrowIfNull(host);
		string address = host.Contains(':', StringComparison.Ordinal) && !host.StartsWith('[') ? $"[{host}]" : host;
		return string.Create(CultureInfo.InvariantCulture, $"{address}:{port}");
	}
}
