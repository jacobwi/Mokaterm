using System.Globalization;

namespace Mokaterm.RdpProbe;

/// <summary>What the command line asked for: <c>host[:port] [user] [domain]</c> and a few switches.</summary>
internal sealed record ProbeArguments
{
	public const int DefaultPort = 3389;

	public required string Host { get; init; }

	public int Port { get; init; } = DefaultPort;

	/// <summary>Null connects without a login, which leaves the login to the server's own screen.</summary>
	public string? User { get; init; }

	public string? Domain { get; init; }

	/// <summary>False sends no login over CredSSP, as the connection option "Network level authentication" off does.</summary>
	public bool NetworkLevelAuthentication { get; init; } = true;

	public int TimeoutSeconds { get; init; } = 20;

	/// <summary>Accepts the server's certificate without asking.</summary>
	public bool AcceptCertificate { get; init; }

	/// <summary>Stops after the connect instead of waiting for the first picture.</summary>
	public bool ConnectOnly { get; init; }

	public static string Usage =>
		"""
		Usage: Mokaterm.RdpProbe host[:port] [user] [domain] [options]

		  host[:port]     the PC to connect to; 3389 unless a port is given ([::1]:3390 for IPv6)
		  user            DOMAIN\name, PCNAME\name, name or name@domain; leave it out to connect without a login
		  domain          a domain for a user name that carries none

		Options:
		  --no-nla        send no login over CredSSP and leave it to the server's own screen
		  --timeout N     seconds for the whole connect (default 20); a certificate question does not count
		  --yes           accept the server's certificate without asking
		  --connect-only  stop once connected instead of waiting for the first picture

		The password comes from MOKATERM_RDP_PASSWORD, or is asked for without echo. It is never printed.
		""";

	/// <summary>The arguments, or null with <paramref name="error"/> set when they do not make sense.</summary>
	public static ProbeArguments? Parse(IReadOnlyList<string> args, out string? error)
	{
		error = null;
		List<string> positional = [];
		bool nla = true;
		bool yes = false;
		bool connectOnly = false;
		int timeout = 20;
		for (int i = 0; i < args.Count; i++)
		{
			switch (args[i])
			{
				case "--no-nla":
					nla = false;
					break;
				case "--yes":
					yes = true;
					break;
				case "--connect-only":
					connectOnly = true;
					break;
				case "--timeout":
					if (i + 1 >= args.Count || !int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out timeout) || timeout is < 1 or > 300)
					{
						error = "--timeout takes a number of seconds from 1 to 300.";
						return null;
					}

					i++;
					break;
				case "-h" or "--help" or "/?":
					return null;
				default:
					if (args[i].StartsWith("--", StringComparison.Ordinal))
					{
						error = $"{args[i]} is not an option this probe knows.";
						return null;
					}

					positional.Add(args[i]);
					break;
			}
		}

		if (positional.Count is < 1 or > 3)
		{
			error = positional.Count == 0 ? "Name the PC to connect to." : "Too many arguments.";
			return null;
		}

		if (!TryReadEndpoint(positional[0], out string host, out int port))
		{
			error = $"{positional[0]} is not a host or host:port.";
			return null;
		}

		return new ProbeArguments
		{
			Host = host,
			Port = port,
			User = positional.Count > 1 ? positional[1] : null,
			Domain = positional.Count > 2 ? positional[2] : null,
			NetworkLevelAuthentication = nla,
			TimeoutSeconds = timeout,
			AcceptCertificate = yes,
			ConnectOnly = connectOnly,
		};
	}

	private static bool TryReadEndpoint(string value, out string host, out int port)
	{
		host = value;
		port = DefaultPort;
		if (value.StartsWith('['))
		{
			int close = value.IndexOf(']', StringComparison.Ordinal);
			if (close < 0)
			{
				return false;
			}

			host = value[1..close];
			string tail = value[(close + 1)..];
			return tail.Length == 0 || (tail.StartsWith(':') && TryReadPort(tail[1..], out port));
		}

		// One colon separates a port; several mean a bare IPv6 address.
		int colon = value.IndexOf(':', StringComparison.Ordinal);
		if (colon >= 0 && value.IndexOf(':', colon + 1) < 0)
		{
			host = value[..colon];
			return host.Length > 0 && TryReadPort(value[(colon + 1)..], out port);
		}

		return host.Length > 0;
	}

	private static bool TryReadPort(string value, out int port) =>
		int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out port) && port is > 0 and <= 65535;
}
