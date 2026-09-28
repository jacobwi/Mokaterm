using System.Globalization;

namespace Mokaterm.Abstractions.Protocols;

/// <summary>
/// The proxy a connection dials through, as a module keeps it in <see cref="ProtocolOptions"/> under its own key prefix
/// (<c>ssh.proxy.</c>, <c>ftp.proxy.</c>). The password is not here: it is a secret and lives on the login's credential.
/// </summary>
public sealed record ProxyOptions
{
	public const int DefaultHttpPort = 8080;

	public const int DefaultSocksPort = 1080;

	/// <summary>SOCKS5 carries a host name as one length-prefixed byte string, so 255 is the hard ceiling.</summary>
	public const int MaxHostLength = 255;

	/// <summary>SOCKS5 and HTTP basic both length-prefix the user name with a single byte.</summary>
	public const int MaxUserLength = 255;

	private const string KindSuffix = "kind";
	private const string HostSuffix = "host";
	private const string PortSuffix = "port";
	private const string UserSuffix = "user";

	public static ProxyOptions None { get; } = new();

	public ProxyKind Kind { get; init; }

	/// <summary>The proxy's own address, never the server's.</summary>
	public string? Host { get; init; }

	/// <summary>Unset uses <see cref="DefaultPortFor"/>.</summary>
	public int? Port { get; init; }

	/// <summary>Unset means a proxy that asks for no login.</summary>
	public string? User { get; init; }

	/// <summary>True when these values name a proxy to dial through. A kind without a host is refused, not ignored.</summary>
	public bool IsEnabled => Kind != ProxyKind.None && !string.IsNullOrWhiteSpace(Host);

	/// <summary>True when the proxy expects a user name and password.</summary>
	public bool NeedsLogin => Kind != ProxyKind.None && !string.IsNullOrWhiteSpace(User);

	public int EffectivePort => Port ?? DefaultPortFor(Kind);

	public static int DefaultPortFor(ProxyKind kind) => kind == ProxyKind.Http ? DefaultHttpPort : DefaultSocksPort;

	public static string KindKey(string keyPrefix) => Key(keyPrefix, KindSuffix);

	public static string HostKey(string keyPrefix) => Key(keyPrefix, HostSuffix);

	public static string PortKey(string keyPrefix) => Key(keyPrefix, PortSuffix);

	public static string UserKey(string keyPrefix) => Key(keyPrefix, UserSuffix);

	public static ProxyOptions From(ProtocolOptions options, string keyPrefix)
	{
		ArgumentNullException.ThrowIfNull(options);
		ProxyKind kind = options.GetEnum(KindKey(keyPrefix), ProxyKind.None);
		if (kind == ProxyKind.None)
		{
			return None;
		}

		int port = options.GetInt32(PortKey(keyPrefix), 0);
		return new ProxyOptions
		{
			Kind = kind,
			Host = TrimToNull(options.GetString(HostKey(keyPrefix))),
			Port = port is >= 1 and <= 65535 ? port : null,
			User = TrimToNull(options.GetString(UserKey(keyPrefix))),
		};
	}

	/// <summary>Writes these values into <paramref name="options"/>. A proxy that is off removes every key of the group.</summary>
	public ProtocolOptions ApplyTo(ProtocolOptions options, string keyPrefix)
	{
		ArgumentNullException.ThrowIfNull(options);
		bool off = Kind == ProxyKind.None;
		return options
			.With(KindKey(keyPrefix), off ? null : Kind.ToString())
			.With(HostKey(keyPrefix), off ? null : TrimToNull(Host))
			.With(PortKey(keyPrefix), off ? null : Port)
			.With(UserKey(keyPrefix), off ? null : TrimToNull(User));
	}

	/// <summary>
	/// Refuses a proxy that cannot be dialled instead of quietly connecting around it: a connection meant to go through a
	/// proxy must not reach its server any other way.
	/// </summary>
	/// <exception cref="ProtocolConnectException">These settings are incomplete or unusable.</exception>
	public void EnsureUsable()
	{
		if (Validate() is { } error)
		{
			throw new ProtocolConnectException(ConnectFailure.ProtocolError, $"This connection cannot be dialled through its proxy: {error}");
		}
	}

	/// <summary>What is wrong with these values, or null when they can be dialled. A proxy that is off is always valid.</summary>
	public string? Validate() =>
		Kind == ProxyKind.None
			? null
			: ValidateHost(Host) ?? ValidatePort(Port) ?? ValidateUser(User);

	/// <summary>What is wrong with a proxy address, or null. Empty is wrong: a proxy that is on needs one.</summary>
	public static string? ValidateHost(string? host)
	{
		string trimmed = host?.Trim() ?? "";
		if (trimmed.Length == 0)
		{
			return "Enter the address of the proxy.";
		}

		return trimmed.Length > MaxHostLength || trimmed.AsSpan().IndexOfAny(" /\\@") >= 0 || trimmed.Any(char.IsControl)
			? "Enter only the hostname or IP address of the proxy, without a user, path or spaces."
			: null;
	}

	/// <summary>What is wrong with a proxy port, or null. Unset is fine and means the default for the kind.</summary>
	public static string? ValidatePort(int? port) =>
		port is { } value && value is < 1 or > 65535
			? "Use a proxy port from 1 to 65535, or leave it empty for the default."
			: null;

	/// <summary>What is wrong with a proxy user, or null. Unset is fine and means a proxy that asks for no login.</summary>
	public static string? ValidateUser(string? user)
	{
		string trimmed = user?.Trim() ?? "";
		return trimmed.Length > MaxUserLength || trimmed.Any(char.IsControl)
			? "The proxy user is longer than a proxy can carry, or holds control characters."
			: null;
	}

	/// <summary><c>host:port</c> for messages. Never used to dial: <see cref="Host"/> and <see cref="EffectivePort"/> are.</summary>
	public string Describe() =>
		string.Create(CultureInfo.InvariantCulture, $"{Host}:{EffectivePort}");

	private static string Key(string keyPrefix, string suffix)
	{
		ArgumentException.ThrowIfNullOrEmpty(keyPrefix);
		return keyPrefix + suffix;
	}

	private static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
