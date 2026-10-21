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

	/// <summary>
	/// With network level authentication off, sends the name and password in the Client Info PDU so Windows signs in by
	/// itself, as the connection option "Sign in with this login's name and password" does. False leaves its sign-in
	/// screen up and asks for no password.
	/// </summary>
	public bool Autologon { get; init; } = true;

	public int TimeoutSeconds { get; init; } = 20;

	/// <summary>How long to wait after the first picture for Windows to say whether it signed in.</summary>
	public int SignInWaitSeconds { get; init; } = 30;

	/// <summary>True when the password goes to the server, so the probe has to ask for it.</summary>
	public bool SendsPassword => User is not null && (NetworkLevelAuthentication || Autologon);

	/// <summary>Accepts the server's certificate without asking.</summary>
	public bool AcceptCertificate { get; init; }

	/// <summary>Stops after the connect instead of waiting for the first picture.</summary>
	public bool ConnectOnly { get; init; }

	public static string Usage =>
		"""
		Usage: Mokaterm.RdpProbe host[:port] [user] [domain] [options]

		  host[:port]       the PC to connect to; 3389 unless a port is given ([::1]:3390 for IPv6)
		  user              DOMAIN\name, PCNAME\name, name, name@domain, AzureAD\you@company.com or
		                    MicrosoftAccount\you@outlook.com; leave it out to connect without a login
		  domain            a domain for a user name that carries none

		Options:
		  --no-nla          skip network level authentication (CredSSP): the name and password go in the
		                    Client Info PDU, inside TLS and after the certificate check, and Windows signs in
		                    by itself, as mstsc does when the PC has network level authentication off
		  --no-autologon    with --no-nla, send no password and stop at the server's own sign-in screen
		  --timeout N       seconds for the whole connect (default 20); a certificate question does not count
		  --sign-in-wait N  seconds to wait after the first picture for Windows to say whether it signed in
		                    (default 30)
		  --yes             accept the server's certificate without asking
		  --connect-only    stop once connected instead of waiting for the first picture

		The password comes from MOKATERM_RDP_PASSWORD, or is asked for without echo. It is never printed.
		""";

	/// <summary>The arguments, or null with <paramref name="error"/> set when they do not make sense.</summary>
	public static ProbeArguments? Parse(IReadOnlyList<string> args, out string? error)
	{
		error = null;
		List<string> positional = [];
		bool nla = true;
		bool autologon = true;
		bool yes = false;
		bool connectOnly = false;
		int timeout = 20;
		int signInWait = 30;
		for (int i = 0; i < args.Count; i++)
		{
			switch (args[i])
			{
				case "--no-nla":
					nla = false;
					break;
				case "--no-autologon":
					autologon = false;
					break;
				case "--yes":
					yes = true;
					break;
				case "--connect-only":
					connectOnly = true;
					break;
				case "--timeout":
					if (!TryReadSeconds(args, i, out timeout))
					{
						error = "--timeout takes a number of seconds from 1 to 300.";
						return null;
					}

					i++;
					break;
				case "--sign-in-wait":
					if (!TryReadSeconds(args, i, out signInWait))
					{
						error = "--sign-in-wait takes a number of seconds from 1 to 300.";
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
			Autologon = autologon,
			TimeoutSeconds = timeout,
			SignInWaitSeconds = signInWait,
			AcceptCertificate = yes,
			ConnectOnly = connectOnly,
		};
	}

	private static bool TryReadSeconds(IReadOnlyList<string> args, int at, out int seconds)
	{
		seconds = 0;
		return at + 1 < args.Count
			&& int.TryParse(args[at + 1], NumberStyles.None, CultureInfo.InvariantCulture, out seconds)
			&& seconds is >= 1 and <= 300;
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
