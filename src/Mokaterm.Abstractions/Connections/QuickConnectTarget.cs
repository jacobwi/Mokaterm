using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Mokaterm.Abstractions.Connections;

/// <summary>
/// Parses the quick-connect box: <c>host</c>, <c>user@host</c>, <c>user@host:2222</c>,
/// <c>ssh://user@host:22</c>, <c>ftp://host</c> and bracketed IPv6 such as <c>root@[::1]:22</c>. A protocol without
/// a network host (a serial port, a local shell) reads the text with <see cref="TryParseName"/> instead.
/// </summary>
public sealed record QuickConnectTarget(string? ProtocolId, string? Username, string Address, int? Port)
{
	/// <summary>Longer than any device path or container name, and short enough that a pasted mistake is caught.</summary>
	public const int MaxNameLength = 255;

	private const string SchemeSeparator = "://";

	/// <summary>
	/// The protocol a <c>scheme://</c> at the start of <paramref name="text"/> names, lowercased, or null when the text
	/// starts with none. Nothing after the scheme is read, so this answers while the rest is still being typed.
	/// </summary>
	public static string? ReadScheme(string? text) =>
		text is not null && TrySplitScheme(text.AsSpan().Trim(), out string? scheme, out _) ? scheme : null;

	public static bool TryParse(string? text, [NotNullWhen(true)] out QuickConnectTarget? target)
	{
		target = null;
		if (string.IsNullOrWhiteSpace(text) || !TrySplitScheme(text.AsSpan().Trim(), out string? protocol, out ReadOnlySpan<char> rest))
		{
			return false;
		}

		if (protocol is not null)
		{
			rest = rest.TrimEnd('/');
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

	/// <summary>
	/// Reads the text for a protocol whose address names something on this computer rather than a network host: a
	/// serial port (<c>COM3</c>, <c>/dev/ttyUSB0</c>), a shell, a container. After an optional <c>scheme://</c> the
	/// whole rest is the address, slashes and colons included, so <c>serial:///dev/ttyUSB0</c> names
	/// <c>/dev/ttyUSB0</c>. Only what no such name holds is refused: nothing at all, whitespace inside it, a control
	/// character, or more than <see cref="MaxNameLength"/> characters. Whether the name exists is the module's question.
	/// </summary>
	public static bool TryParseName(string? text, [NotNullWhen(true)] out QuickConnectTarget? target)
	{
		target = null;
		if (string.IsNullOrWhiteSpace(text) || !TrySplitScheme(text.AsSpan().Trim(), out string? protocol, out ReadOnlySpan<char> rest))
		{
			return false;
		}

		if (rest.IsEmpty || rest.Length > MaxNameLength)
		{
			return false;
		}

		foreach (char c in rest)
		{
			if (char.IsWhiteSpace(c) || char.IsControl(c))
			{
				return false;
			}
		}

		target = new QuickConnectTarget(protocol, null, rest.ToString(), null);
		return true;
	}

	/// <summary>
	/// Splits off a leading <c>scheme://</c>. False when the text has a separator but no usable scheme before it, which
	/// no protocol could be named by.
	/// </summary>
	private static bool TrySplitScheme(ReadOnlySpan<char> text, out string? scheme, out ReadOnlySpan<char> rest)
	{
		scheme = null;
		rest = text;
		int schemeEnd = text.IndexOf(SchemeSeparator, StringComparison.Ordinal);
		if (schemeEnd < 0)
		{
			return true;
		}

		ReadOnlySpan<char> name = text[..schemeEnd];
		if (name.IsEmpty || !IsSchemeName(name))
		{
			return false;
		}

		scheme = name.ToString().ToLowerInvariant();
		rest = text[(schemeEnd + SchemeSeparator.Length)..];
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
