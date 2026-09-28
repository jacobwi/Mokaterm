using System.Globalization;

namespace Mokaterm.Tests.Shared;

/// <summary>
/// What a live test reads from its environment variable: <c>host</c>, <c>host:port</c>, <c>user@host</c>,
/// <c>user:password@host:port</c> or <c>user:password@[::1]:port</c>, followed by an optional <c>?name=value</c> query
/// the caller reads its own keys from. The login is percent-decoded, so a password can hold a colon or an at sign, and
/// a value that does not parse is refused instead of turning into a host nobody named.
/// </summary>
internal sealed record LiveEndpoint
{
	private static readonly IReadOnlyDictionary<string, string> NoQuery =
		new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	public required string Host { get; init; }

	/// <summary>The port the value named, or the default the caller passed.</summary>
	public required int Port { get; init; }

	/// <summary>False when the value named no port, so a caller whose default depends on the query can work one out.</summary>
	public required bool PortWasNamed { get; init; }

	public string? User { get; init; }

	public string? Password { get; init; }

	public IReadOnlyDictionary<string, string> Query { get; init; } = NoQuery;

	/// <summary>The variable's value, or a skip when it is not set.</summary>
	public static string Require(string variable, string skipMessage)
	{
		if (Environment.GetEnvironmentVariable(variable) is not { Length: > 0 } value || string.IsNullOrWhiteSpace(value))
		{
			Assert.Skip(skipMessage);
			throw new InvalidOperationException("Assert.Skip returned.");
		}

		return value;
	}

	public static LiveEndpoint Parse(string value, int defaultPort)
	{
		ArgumentNullException.ThrowIfNull(value);
		string rest = value.Trim();
		if (rest.Length == 0)
		{
			throw new FormatException("A live test's endpoint must not be empty.");
		}

		IReadOnlyDictionary<string, string> query = NoQuery;
		int mark = rest.IndexOf('?', StringComparison.Ordinal);
		if (mark >= 0)
		{
			query = ReadQuery(rest[(mark + 1)..]);
			rest = rest[..mark];
		}

		string? user = null;
		string? password = null;
		int at = rest.LastIndexOf('@');
		if (at >= 0)
		{
			string login = rest[..at];
			rest = rest[(at + 1)..];
			int colon = login.IndexOf(':', StringComparison.Ordinal);
			user = Empty(Uri.UnescapeDataString(colon < 0 ? login : login[..colon]));
			password = colon < 0 ? null : Empty(Uri.UnescapeDataString(login[(colon + 1)..]));
		}

		(string host, int? port) = ReadHost(rest);
		return new LiveEndpoint
		{
			Host = host,
			Port = port ?? defaultPort,
			PortWasNamed = port is not null,
			User = user,
			Password = password,
			Query = query,
		};
	}

	/// <summary>The query value for <paramref name="name"/>, or null when the value named none.</summary>
	public string? Option(string name) => Query.TryGetValue(name, out string? value) ? value : null;

	/// <summary>A query value read as <typeparamref name="TEnum"/>. A name that is not a member is refused, not ignored.</summary>
	public TEnum Option<TEnum>(string name, TEnum fallback)
		where TEnum : struct, Enum
	{
		if (Option(name) is not { Length: > 0 } value)
		{
			return fallback;
		}

		TEnum parsed = Enum.Parse<TEnum>(value, ignoreCase: true);
		return Enum.IsDefined(parsed) ? parsed : throw new FormatException($"{value} is not a {typeof(TEnum).Name}.");
	}

	/// <summary>A switch: named with anything but <c>false</c> turns it on, and naming it at all is enough.</summary>
	public bool Flag(string name, bool fallback = false) =>
		Option(name) is { } value ? !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) : fallback;

	/// <summary>The login, refusing a value that named none: most protocols cannot connect without one.</summary>
	public (string User, string Password) RequireLogin(string variable) =>
		User is null || Password is null
			? throw new FormatException($"{variable} must look like user:password@host:port.")
			: (User, Password);

	private static (string Host, int? Port) ReadHost(string endpoint)
	{
		if (endpoint.StartsWith('['))
		{
			int close = endpoint.IndexOf(']', StringComparison.Ordinal);
			if (close < 0)
			{
				throw new FormatException($"{endpoint} is missing the closing bracket of its address.");
			}

			string address = endpoint[1..close];
			string tail = endpoint[(close + 1)..];
			if (tail.Length == 0)
			{
				return (Named(address), null);
			}

			return tail.StartsWith(':')
				? (Named(address), ReadPort(tail[1..]))
				: throw new FormatException($"{endpoint} has {tail} where its port should be.");
		}

		// One colon separates a port; several mean a bare IPv6 address, which has to be bracketed to carry one.
		int separator = endpoint.IndexOf(':', StringComparison.Ordinal);
		if (separator < 0 || endpoint.IndexOf(':', separator + 1) >= 0)
		{
			return (Named(endpoint), null);
		}

		return (Named(endpoint[..separator]), ReadPort(endpoint[(separator + 1)..]));
	}

	private static int ReadPort(string value) =>
		int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int port) && port is > 0 and <= 65535
			? port
			: throw new FormatException($"{value} is not a port number.");

	private static string Named(string host) =>
		host.Length > 0 ? host : throw new FormatException("A live test's endpoint must name a host.");

	private static string? Empty(string value) => value.Length > 0 ? value : null;

	private static Dictionary<string, string> ReadQuery(string query)
	{
		Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
		foreach (string pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
		{
			string[] parts = pair.Split('=', 2);
			values[Uri.UnescapeDataString(parts[0]).Trim()] = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]).Trim() : "";
		}

		return values;
	}
}
