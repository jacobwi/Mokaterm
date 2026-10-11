using System.Globalization;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// Reads the ports column of <c>docker ps</c>: <c>0.0.0.0:8080-&gt;80/tcp, :::8080-&gt;80/tcp, 443/tcp</c>. The same mapping on
/// IPv4 and IPv6 is one port to the user, so it is listed once.
/// </summary>
internal static class DockerPorts
{
	public static IReadOnlyList<DockerPort> Parse(string? text)
	{
		List<DockerPort> ports = [];
		HashSet<string> seen = new(StringComparer.Ordinal);
		foreach (string entry in (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			if (ParseOne(entry) is not { } port)
			{
				continue;
			}

			// 0.0.0.0 and :: carry the same mapping; a port bound to one address only stays apart.
			bool everywhere = port.HostAddress is "0.0.0.0" or "::" or "[::]";
			string key = string.Create(CultureInfo.InvariantCulture, $"{(everywhere ? "*" : port.HostAddress)}|{port.HostPort}|{port.ContainerPort}|{port.Protocol}");
			if (seen.Add(key))
			{
				ports.Add(port);
			}
		}

		return ports;
	}

	private static DockerPort? ParseOne(string entry)
	{
		string host = "";
		string? hostPort = null;
		string inside = entry;
		int arrow = entry.IndexOf("->", StringComparison.Ordinal);
		if (arrow >= 0)
		{
			string published = entry[..arrow];
			inside = entry[(arrow + 2)..];

			// The address may be IPv6 (":::8080" or "[::]:8080"), so the port is after the last colon.
			int colon = published.LastIndexOf(':');
			if (colon < 0)
			{
				return null;
			}

			host = published[..colon];
			hostPort = published[(colon + 1)..];
		}

		int slash = inside.IndexOf('/', StringComparison.Ordinal);
		string containerPort = slash < 0 ? inside : inside[..slash];
		string protocol = slash < 0 ? "tcp" : inside[(slash + 1)..];
		return containerPort.Length == 0 ? null : new DockerPort(host, hostPort, containerPort, protocol);
	}
}
