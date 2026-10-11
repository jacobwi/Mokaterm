using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace Mokaterm.Modules.Ssh.Forwarding;

/// <summary>
/// Where the local X server listens, written the way <c>DISPLAY</c> is: <c>host:display.screen</c>. Display N is TCP
/// port 6000 + N, and an empty host or <c>localhost</c> means 127.0.0.1, which is where VcXsrv, Xming and X410 listen.
/// Unix socket displays (<c>unix:0</c>, a path) are refused: this client reaches X servers over TCP only.
/// </summary>
internal readonly record struct X11Display(string Host, int Number, int Screen)
{
	public const int FirstPort = 6000;

	/// <summary>The highest display whose port still fits in 16 bits.</summary>
	public const int MaxNumber = ushort.MaxValue - FirstPort;

	public const int MaxScreen = 255;

	public const string DefaultText = "localhost:0.0";

	private const string LocalHostName = "localhost";

	private const int MaxHostLength = 253;

	public static X11Display Default { get; } = new(LocalHostName, 0, 0);

	public int Port => FirstPort + Number;

	/// <summary>True when the X server is on this machine, which is what the Xauthority lookup keys on.</summary>
	public bool IsLocal =>
		string.Equals(Host, LocalHostName, StringComparison.OrdinalIgnoreCase)
		|| (IPAddress.TryParse(Host, out IPAddress? address) && IPAddress.IsLoopback(address));

	/// <summary>
	/// The address to connect to. <c>localhost</c> is 127.0.0.1 rather than a name lookup, which may answer ::1 first
	/// while the X server only listens on IPv4.
	/// </summary>
	public EndPoint EndPoint =>
		string.Equals(Host, LocalHostName, StringComparison.OrdinalIgnoreCase)
			? new IPEndPoint(IPAddress.Loopback, Port)
			: IPAddress.TryParse(Host, out IPAddress? address)
				? new IPEndPoint(address, Port)
				: new DnsEndPoint(Host, Port);

	/// <summary>Reads a display. Blank text is <see cref="Default"/>; text that is not a TCP display gives a reason.</summary>
	public static bool TryParse(string? text, out X11Display display, out string? error)
	{
		display = Default;
		error = null;
		if (string.IsNullOrWhiteSpace(text))
		{
			return true;
		}

		string value = text.Trim();
		string host;
		string rest;
		if (value.StartsWith('['))
		{
			int close = value.IndexOf("]:", StringComparison.Ordinal);
			if (close < 0)
			{
				return Fail("Write an IPv6 address as [address]:display, such as [::1]:0.", out error);
			}

			host = value[1..close];
			rest = value[(close + 2)..];
			if (!IPAddress.TryParse(host, out IPAddress? v6) || v6.AddressFamily != AddressFamily.InterNetworkV6)
			{
				return Fail("Only an IPv6 address goes in brackets.", out error);
			}
		}
		else
		{
			int colon = value.LastIndexOf(':');
			if (colon < 0)
			{
				return Fail("Use host:display, such as localhost:0.0.", out error);
			}

			host = value[..colon];
			rest = value[(colon + 1)..];
			if (host.Contains(':', StringComparison.Ordinal))
			{
				return Fail("Write an IPv6 address in brackets, such as [::1]:0.", out error);
			}

			if (string.Equals(host, "unix", StringComparison.OrdinalIgnoreCase) || host.Contains('/', StringComparison.Ordinal))
			{
				return Fail("Unix socket displays are not supported. Use a TCP display, such as localhost:0.", out error);
			}

			if (host.Length == 0)
			{
				host = LocalHostName;
			}
			else if (!IsHostName(host))
			{
				return Fail("The host can hold letters, digits, dots and dashes.", out error);
			}
		}

		string numberText = rest;
		string screenText = "0";
		int dot = rest.IndexOf('.', StringComparison.Ordinal);
		if (dot >= 0)
		{
			numberText = rest[..dot];
			screenText = rest[(dot + 1)..];
		}

		if (!TryReadNumber(numberText, MaxNumber, out int number))
		{
			return Fail(string.Create(CultureInfo.InvariantCulture, $"The display is a number from 0 to {MaxNumber}."), out error);
		}

		if (!TryReadNumber(screenText, MaxScreen, out int screen))
		{
			return Fail(string.Create(CultureInfo.InvariantCulture, $"The screen is a number from 0 to {MaxScreen}."), out error);
		}

		display = new X11Display(host, number, screen);
		return true;
	}

	/// <summary><c>host:display.screen</c>, with an IPv6 host in brackets.</summary>
	public override string ToString()
	{
		string host = IPAddress.TryParse(Host, out IPAddress? address) && address.AddressFamily == AddressFamily.InterNetworkV6
			? $"[{Host}]"
			: Host;
		return string.Create(CultureInfo.InvariantCulture, $"{host}:{Number}.{Screen}");
	}

	/// <summary>The socket address, for messages: <c>127.0.0.1:6000</c>.</summary>
	public string DescribeEndPoint() => EndPoint switch
	{
		IPEndPoint ip => ip.ToString(),
		DnsEndPoint dns => string.Create(CultureInfo.InvariantCulture, $"{dns.Host}:{dns.Port}"),
		EndPoint other => other.ToString() ?? "",
	};

	private static bool TryReadNumber(string text, int max, out int value)
	{
		value = 0;
		return text.Length is > 0 and <= 5
			&& text.All(char.IsAsciiDigit)
			&& int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value)
			&& value <= max;
	}

	private static bool IsHostName(string host) =>
		host.Length <= MaxHostLength
		&& host.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-')
		&& !host.StartsWith('.')
		&& !host.StartsWith('-');

	private static bool Fail(string message, out string? error)
	{
		error = message;
		return false;
	}
}
