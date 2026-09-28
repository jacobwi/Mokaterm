using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Mokaterm.Abstractions.Connections;

/// <summary>
/// Parses the quick-connect box: <c>host</c>, <c>user@host</c>, <c>user@host:2222</c>,
/// <c>ssh://user@host:22</c>, <c>ftp://host</c> and bracketed IPv6 such as <c>root@[::1]:22</c>.
/// </summary>
public sealed record QuickConnectTarget(string? ProtocolId, string? Username, string Address, int? Port)
{
	public static bool TryParse(string? text, [NotNullWhen(true)] out QuickConnectTarget? target)
	{
		target = null;
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}

		ReadOnlySpan<char> rest = text.AsSpan().Trim();
		string? protocol = null;

		int schemeEnd = rest.IndexOf("://", StringComparison.Ordinal);
		if (schemeEnd >= 0)
		{
			ReadOnlySpan<char> scheme = rest[..schemeEnd];
			if (scheme.IsEmpty || !IsSchemeName(scheme))
			{
				return false;
			}

			protocol = scheme.ToString().ToLowerInvariant();
			rest = rest[(schemeEnd + 3)..].TrimEnd('/');
		}

		string? username = null;
		int at = rest.LastIndexOf('@');
		if (at >= 0)
		{
			if (at == 0)
			{
				return false;
			}

			username = rest[..at].ToString();
			rest = rest[(at + 1)..];
		}

		string address;
		int? port = null;
		if (rest.StartsWith("["))
		{
			int close = rest.IndexOf(']');
			if (close < 2)
			{
				return false;
			}

			address = rest[1..close].ToString();
			ReadOnlySpan<char> after = rest[(close + 1)..];
			if (!after.IsEmpty)
			{
				if (after[0] != ':' || !TryParsePort(after[1..], out int bracketPort))
				{
					return false;
				}

				port = bracketPort;
			}
		}
		else
		{
			int colon = rest.LastIndexOf(':');
			// More than one colon without brackets is a bare IPv6 address with no port.
			if (colon >= 0 && rest[..colon].IndexOf(':') < 0)
			{
				if (!TryParsePort(rest[(colon + 1)..], out int parsedPort))
				{
					return false;
				}

				port = parsedPort;
				rest = rest[..colon];
			}

			address = rest.ToString();
		}

		if (address.Length == 0 || address.AsSpan().IndexOfAny(" /\\@") >= 0)
		{
			return false;
		}

		target = new QuickConnectTarget(protocol, username, address, port);
		return true;
	}

	private static bool TryParsePort(ReadOnlySpan<char> text, out int port) =>
		int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out port) && port is > 0 and <= 65535;

	private static bool IsSchemeName(ReadOnlySpan<char> scheme)
	{
		foreach (char c in scheme)
		{
			if (!char.IsAsciiLetterOrDigit(c) && c is not ('+' or '-' or '.'))
			{
				return false;
			}
		}

		return true;
	}
}
