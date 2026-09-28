using Mokaterm.Abstractions.Settings;

namespace Mokaterm.Modules.Mqtt;


/// <summary>
/// What every MQTT session shares, stored under <c>mqtt</c> in settings.json. Most of it is the bound on what one
/// session may hold: a broker can push megabytes a second, and the explorer keeps what it shows.
/// </summary>
public sealed record MqttSettings : ISettingsSection
{
	public const int MinConnectTimeoutSeconds = 1;

	public const int MaxConnectTimeoutSeconds = 300;

	public const int MaxAuthenticationAttempts = 10;

	public const int MinMessagesPerTopic = 1;

	public const int MaxMessagesPerTopic = 10_000;

	public const int MinTotalMessages = 50;

	public const int MaxTotalMessages = 200_000;

	public const int MinPayloadKilobytes = 1;

	public const int MaxPayloadKilobytes = 8192;

	public const int MinTotalPayloadKilobytes = 64;

	public const int MaxTotalPayloadKilobytes = 1_048_576;

	public const int MinTopics = 16;

	public const int MaxTopics = 200_000;

	public static string SectionKey => "mqtt";

	/// <summary>How long to wait for the socket, the TLS handshake and the broker's CONNACK together.</summary>
	public int ConnectTimeoutSeconds { get; init; } = 15;

	/// <summary>
	/// Password prompts, the first one included, before a refused login is final. A broker answers a bad password by
	/// refusing the whole connection, so every retry is a new connection.
	/// </summary>
	public int AuthenticationAttempts { get; init; } = 3;

	/// <summary>Messages kept for one topic. The oldest goes when a topic passes it.</summary>
	public int MessagesPerTopic { get; init; } = 50;

	/// <summary>Messages kept across every topic of one session. The oldest anywhere goes when the session passes it.</summary>
	public int TotalMessages { get; init; } = 2000;

	/// <summary>
	/// Largest payload kept, in kilobytes. A bigger one is kept cut to this and marked, so the topic and the time
	/// still show without holding a firmware image in memory.
	/// </summary>
	public int LargestPayloadKilobytes { get; init; } = 32;

	/// <summary>
	/// Payload bytes one session may hold, in kilobytes. This is the cap that actually bounds the memory, since
	/// the message counts alone would allow their product.
	/// </summary>
	public int TotalPayloadKilobytes { get; init; } = 16_384;

	/// <summary>
	/// Topics one session's tree may hold. A broker that puts an id in every topic would otherwise grow it without
	/// end; past this, messages on new topics are counted and dropped.
	/// </summary>
	public int MaxTopicCount { get; init; } = 2000;

	/// <summary>What the publish form and a new subscription start at.</summary>
	public MqttQos DefaultQualityOfService { get; init; } = MqttQos.AtMostOnce;

	/// <summary>Show the topic tree with the newest topics first instead of in name order.</summary>
	public bool SortTopicsByTime { get; init; }

	internal TimeSpan ConnectTimeout => TimeSpan.FromSeconds(ConnectTimeoutSeconds);

	/// <summary>A copy with every value inside the range the settings page allows.</summary>
	public MqttSettings Clamped() => this with
	{
		ConnectTimeoutSeconds = Math.Clamp(ConnectTimeoutSeconds, MinConnectTimeoutSeconds, MaxConnectTimeoutSeconds),
		AuthenticationAttempts = Math.Clamp(AuthenticationAttempts, 1, MaxAuthenticationAttempts),
		MessagesPerTopic = Math.Clamp(MessagesPerTopic, MinMessagesPerTopic, MaxMessagesPerTopic),
		TotalMessages = Math.Clamp(TotalMessages, MinTotalMessages, MaxTotalMessages),
		LargestPayloadKilobytes = Math.Clamp(LargestPayloadKilobytes, MinPayloadKilobytes, MaxPayloadKilobytes),
		TotalPayloadKilobytes = Math.Clamp(TotalPayloadKilobytes, MinTotalPayloadKilobytes, MaxTotalPayloadKilobytes),
		MaxTopicCount = Math.Clamp(MaxTopicCount, MinTopics, MaxTopics),
		DefaultQualityOfService = Enum.IsDefined(DefaultQualityOfService) ? DefaultQualityOfService : MqttQos.AtMostOnce,
	};

	/// <summary>The caps as the message store takes them, already clamped.</summary>
	internal MqttStoreCaps Caps()
	{
		MqttSettings clamped = Clamped();
		return new MqttStoreCaps
		{
			MessagesPerTopic = clamped.MessagesPerTopic,
			TotalMessages = clamped.TotalMessages,
			MaxPayloadBytes = clamped.LargestPayloadKilobytes * 1024L,
			TotalPayloadBytes = clamped.TotalPayloadKilobytes * 1024L,
			MaxTopics = clamped.MaxTopicCount,
		};
	}
}
