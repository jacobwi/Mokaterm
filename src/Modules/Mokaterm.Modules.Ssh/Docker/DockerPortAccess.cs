using System.Collections.Frozen;
using System.Globalization;
using System.Net;
using Mokaterm.Modules.Ssh.Tunnels;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// How the user's browser reaches a container's published port. On the desktop the browser runs where the connection
/// starts, so a tunnel through the session reaches whatever the docker host itself can. On the web host the connection
/// starts on the server, where a tunnel's 127.0.0.1 would be the server's own, so only a port published on every
/// interface opens, straight at the session's host.
/// </summary>
internal static class DockerPortAccess
{
	/// <summary>The loopback address a tunnel goes to for a port published on every interface.</summary>
	public const string AnyInterfaceTarget = "127.0.0.1";

	private static readonly FrozenSet<int> TlsPorts = FrozenSet.Create(443, 8443, 9443);

	// Ports web servers and the usual dashboards listen on in plain http. Anything else is the user's call.
	private static readonly FrozenSet<int> PlainPorts = FrozenSet.Create(80, 3000, 5000, 5173, 5601, 8000, 8008, 8080, 8081, 8088, 8888, 9000, 9090, 15672);

	/// <summary>
	/// The ports the Open button offers: published TCP ports with a single host port, each mapping once. A range has
	/// no one port to open, and udp has no page.
	/// </summary>
	public static IReadOnlyList<DockerPort> Openable(IEnumerable<DockerPort> ports)
	{
		ArgumentNullException.ThrowIfNull(ports);
		List<DockerPort> openable = [];
		HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
		foreach (DockerPort port in ports)
		{
			if (!IsTcp(port) || SinglePort(port.HostPort) is not { } hostPort)
			{
				continue;
			}

			string address = IsEveryInterface(port.HostAddress) ? "*" : Unbracket(port.HostAddress);
			if (seen.Add(string.Create(CultureInfo.InvariantCulture, $"{address}|{hostPort}")))
			{
				openable.Add(port);
			}
		}

		return openable;
	}

	/// <param name="desktop">True when the browser runs on the machine the connection starts from.</param>
	/// <param name="sessionHost">The server's address as the login names it.</param>
	/// <param name="throughJumpHosts">True when the login reaches the server through jump hosts.</param>
	public static DockerPortRoute Route(DockerPort port, bool desktop, string sessionHost, bool throughJumpHosts)
	{
		ArgumentNullException.ThrowIfNull(port);
		ArgumentNullException.ThrowIfNull(sessionHost);
		if (!IsTcp(port) || SinglePort(port.HostPort) is not { } hostPort)
		{
			return Refused(port, "Only a TCP port published on one host port opens in the browser.");
		}

		IReadOnlyList<string> schemes = SchemesFor(port, hostPort);
		string address = Unbracket(port.HostAddress);
		bool everywhere = IsEveryInterface(port.HostAddress);
		if (desktop)
		{
			string target = everywhere ? AnyInterfaceTarget : address;
			return SshTunnelDefinition.IsValidHost(target)
				? new DockerPortRoute { Port = port, Kind = DockerPortRouteKind.Tunnel, Host = target, HostPort = hostPort, Schemes = schemes }
				: Refused(port, "Docker names an address for it that a tunnel cannot go to.");
		}

		if (!everywhere)
		{
			return Refused(port, IsLoopback(address)
				? $"It listens on {address} only, so only the server itself reaches it. The web version opens no tunnels: one would end on the web server, not on your machine."
				: $"It listens on {address} only, not on every interface, so the browser cannot reach it at {sessionHost}.");
		}

		if (IsLoopback(Unbracket(sessionHost)))
		{
			return Refused(port, $"This login connects to {sessionHost}, which is the web server itself. In the browser that address is your own computer.");
		}

		return throughJumpHosts
			? Refused(port, $"This login reaches {sessionHost} through jump hosts, so the browser cannot reach it there directly.")
			: new DockerPortRoute { Port = port, Kind = DockerPortRouteKind.Direct, Host = sessionHost, HostPort = hostPort, Schemes = schemes };
	}

	/// <summary>
	/// <c>https</c> for the well-known TLS ports, <c>http</c> for the usual web ports, both when neither says. The port
	/// inside the container decides first, since it is the one the server picked; the published one is often arbitrary.
	/// </summary>
	public static IReadOnlyList<string> SchemesFor(DockerPort port, int hostPort)
	{
		ArgumentNullException.ThrowIfNull(port);
		foreach (int? candidate in (ReadOnlySpan<int?>)[SinglePort(port.ContainerPort), hostPort])
		{
			if (candidate is not { } known)
			{
				continue;
			}

			if (TlsPorts.Contains(known))
			{
				return [Uri.UriSchemeHttps];
			}

			if (PlainPorts.Contains(known))
			{
				return [Uri.UriSchemeHttp];
			}
		}

		return [Uri.UriSchemeHttp, Uri.UriSchemeHttps];
	}

	/// <summary>The port in <paramref name="text"/>, or null for a range, a blank or anything outside 1 to 65535.</summary>
	public static int? SinglePort(string? text) =>
		int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int port) && port is >= 1 and <= 65535 ? port : null;

	/// <summary>True for an address docker writes for a port published on every interface.</summary>
	public static bool IsEveryInterface(string? address) => address is "" or null or "0.0.0.0" or "::" or "[::]";

	private static bool IsTcp(DockerPort port) => string.Equals(port.Protocol, "tcp", StringComparison.OrdinalIgnoreCase);

	private static bool IsLoopback(string address) =>
		string.Equals(address, "localhost", StringComparison.OrdinalIgnoreCase)
		|| (IPAddress.TryParse(address, out IPAddress? parsed) && IPAddress.IsLoopback(parsed));

	private static string Unbracket(string address) =>
		address.Length > 1 && address[0] == '[' && address[^1] == ']' ? address[1..^1] : address;

	private static DockerPortRoute Refused(DockerPort port, string reason) =>
		new() { Port = port, Kind = DockerPortRouteKind.Refused, Reason = reason };
}
