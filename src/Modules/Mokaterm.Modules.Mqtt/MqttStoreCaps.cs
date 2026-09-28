namespace Mokaterm.Modules.Mqtt;

/// <summary>
/// What one session's message store may hold. Read once when the session opens, so changing the settings leaves
/// open sessions alone.
/// </summary>
internal sealed record MqttStoreCaps
{
	public required int MessagesPerTopic { get; init; }

	public required int TotalMessages { get; init; }

	/// <summary>Payload bytes kept for one message. More than this is kept cut and marked.</summary>
	public required long MaxPayloadBytes { get; init; }

	/// <summary>Payload bytes kept across the session. The oldest message goes until the store is under it.</summary>
	public required long TotalPayloadBytes { get; init; }

	public required int MaxTopics { get; init; }
}
