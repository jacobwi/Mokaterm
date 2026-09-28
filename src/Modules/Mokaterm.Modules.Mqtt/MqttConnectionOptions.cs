using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Modules.Mqtt;

/// <summary>Typed access to the <c>mqtt.*</c> keys an MQTT connection keeps in its <see cref="ProtocolOptions"/>.</summary>
public sealed record MqttConnectionOptions
{
	public const string TransportKey = "mqtt.transport";

	public const string TlsKey = "mqtt.tls";

	public const string PathKey = "mqtt.path";

	public const string ClientIdKey = "mqtt.clientId";

	public const string CleanStartKey = "mqtt.cleanStart";

	public const string KeepAliveKey = "mqtt.keepAliveSeconds";

	public const string ProtocolKey = "mqtt.protocol";

	/// <summary>One key per subscription: <c>mqtt.sub.00</c>, <c>mqtt.sub.01</c> and so on.</summary>
	public const string SubscriptionKeyPrefix = "mqtt.sub.";

	/// <summary>What the IANA registry calls <c>mqtt</c>.</summary>
	public const int PlainPort = 1883;

	/// <summary>What the IANA registry calls <c>secure-mqtt</c>.</summary>
	public const int TlsPort = 8883;

	/// <summary>
	/// No port is registered for MQTT over WebSockets, and every broker picks its own (8083, 8000, 9001), so an
	/// empty port falls back to the one the URL scheme implies.
	/// </summary>
	public const int WebSocketPort = 80;

	public const int WebSocketTlsPort = 443;

	public const string DefaultPath = "/mqtt";

	public const int MaxClientIdLength = 128;

	public const int MaxPathLength = 256;

	/// <summary>The CONNECT keep-alive field is a 16-bit count of seconds.</summary>
	public const int MaxKeepAliveSeconds = 65535;

	public const int DefaultKeepAliveSeconds = 60;

	public const int MaxSubscriptions = 64;

	public static MqttConnectionOptions Default { get; } = new();

	public MqttTransport Transport { get; init; }

	/// <summary>TLS for the whole connection. The broker's certificate goes through the host verifier either way.</summary>
	public bool UseTls { get; init; }

	/// <summary>The path the WebSocket URL ends in, which brokers almost always publish as <c>/mqtt</c>.</summary>
	public string Path { get; init; } = DefaultPath;

	/// <summary>
	/// The name the broker lists this connection under. Empty asks for a generated one, because two sessions
	/// sharing a client id take each other's connection down.
	/// </summary>
	public string? ClientId { get; init; }

	/// <summary>Start without the broker's stored session, so no queued messages arrive from a previous run.</summary>
	public bool CleanStart { get; init; } = true;

	/// <summary>Idle seconds before a PINGREQ goes out. 0 turns keep-alive off, which lets a dead link go unnoticed.</summary>
	public int KeepAliveSeconds { get; init; } = DefaultKeepAliveSeconds;

	public MqttProtocolLevel Protocol { get; init; }

	/// <summary>Filters this connection subscribes to, in the order they were added.</summary>
	public IReadOnlyList<MqttSubscription> Subscriptions { get; init; } = [];

	/// <summary>What an empty port field means for these options.</summary>
	public int DefaultPort => Transport switch
	{
		MqttTransport.WebSocket => UseTls ? WebSocketTlsPort : WebSocketPort,
		_ => UseTls ? TlsPort : PlainPort,
	};

	public static MqttConnectionOptions From(ProtocolOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		string? clientId = options.GetString(ClientIdKey);
		string? path = options.GetString(PathKey);
		return new MqttConnectionOptions
		{
			Transport = options.GetEnum(TransportKey, MqttTransport.Tcp),
			UseTls = options.GetBoolean(TlsKey, false),
			Path = IsValidPath(path) ? path : DefaultPath,
			ClientId = IsValidClientId(clientId) ? clientId : null,
			CleanStart = options.GetBoolean(CleanStartKey, true),
			KeepAliveSeconds = ClampKeepAlive(options.GetInt32(KeepAliveKey, DefaultKeepAliveSeconds)),
			Protocol = options.GetEnum(ProtocolKey, MqttProtocolLevel.V500),
			Subscriptions = ParseSubscriptions(options),
		};
	}

	/// <summary>Writes these values over <paramref name="options"/>, keeping keys that belong to anything else.</summary>
	public ProtocolOptions ApplyTo(ProtocolOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		// The two enums are written even at their default, the way the other modules write theirs: a stored name keeps
		// its meaning if a future version picks a different default, where a missing key would silently change.
		ProtocolOptions written = options
			.WithEnum<MqttTransport>(TransportKey, Transport)
			.With(TlsKey, UseTls ? "true" : null)
			.With(PathKey, IsValidPath(Path) && Path != DefaultPath ? Path : null)
			.With(ClientIdKey, IsValidClientId(ClientId) ? ClientId : null)
			.With(CleanStartKey, CleanStart ? null : "false")
			.With(KeepAliveKey, ClampKeepAlive(KeepAliveSeconds) == DefaultKeepAliveSeconds ? null : ClampKeepAlive(KeepAliveSeconds))
			.WithEnum<MqttProtocolLevel>(ProtocolKey, Protocol);

		// Every subscription key is rewritten, so removing one leaves no gap behind.
		foreach (string key in written.Keys.Where(IsSubscriptionKey).ToArray())
		{
			written = written.With(key, (string?)null);
		}

		int index = 0;
		foreach (MqttSubscription subscription in Subscriptions.Take(MaxSubscriptions))
		{
			if (subscription.Validate() is null)
			{
				written = written.With(SubscriptionKey(index), subscription.Format());
				index++;
			}
		}

		return written;
	}

	/// <summary>True for client ids a broker accepts: printable, one line and short enough to read in a client list.</summary>
	public static bool IsValidClientId([NotNullWhen(true)] string? clientId) =>
		!string.IsNullOrWhiteSpace(clientId)
		&& clientId.Length <= MaxClientIdLength
		&& !clientId.Any(char.IsControl)
		&& !clientId.Any(char.IsWhiteSpace);

	/// <summary>True for a WebSocket path: one leading slash, no query and nothing that needs escaping.</summary>
	public static bool IsValidPath([NotNullWhen(true)] string? path) =>
		path is { Length: > 0 }
		&& path.Length <= MaxPathLength
		&& path[0] == '/'
		&& !path.Any(char.IsControl)
		&& !path.Any(char.IsWhiteSpace)
		&& path.IndexOfAny(['?', '#']) < 0;

	public bool Equals(MqttConnectionOptions? other) =>
		other is not null
		&& Transport == other.Transport
		&& UseTls == other.UseTls
		&& Path == other.Path
		&& ClientId == other.ClientId
		&& CleanStart == other.CleanStart
		&& KeepAliveSeconds == other.KeepAliveSeconds
		&& Protocol == other.Protocol
		&& Subscriptions.SequenceEqual(other.Subscriptions);

	public override int GetHashCode()
	{
		HashCode hash = default;
		hash.Add(Transport);
		hash.Add(UseTls);
		hash.Add(Path);
		hash.Add(ClientId);
		hash.Add(CleanStart);
		hash.Add(KeepAliveSeconds);
		hash.Add(Protocol);
		foreach (MqttSubscription subscription in Subscriptions)
		{
			hash.Add(subscription);
		}

		return hash.ToHashCode();
	}

	private static int ClampKeepAlive(int seconds) => Math.Clamp(seconds, 0, MaxKeepAliveSeconds);

	private static string SubscriptionKey(int index) =>
		SubscriptionKeyPrefix + index.ToString("D2", CultureInfo.InvariantCulture);

	private static bool IsSubscriptionKey(string key) => key.StartsWith(SubscriptionKeyPrefix, StringComparison.Ordinal);

	private static List<MqttSubscription> ParseSubscriptions(ProtocolOptions options)
	{
		List<MqttSubscription> subscriptions = [];
		foreach (KeyValuePair<string, string> entry in options.Where(entry => IsSubscriptionKey(entry.Key)).OrderBy(entry => entry.Key, StringComparer.Ordinal))
		{
			if (subscriptions.Count >= MaxSubscriptions)
			{
				break;
			}

			// A value this version cannot read is dropped rather than shown as a broken row.
			if (MqttSubscription.Parse(entry.Value) is { } subscription && !subscriptions.Exists(existing => existing.Id == subscription.Id))
			{
				subscriptions.Add(subscription);
			}
		}

		return subscriptions;
	}
}
