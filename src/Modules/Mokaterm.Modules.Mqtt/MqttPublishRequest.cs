using System.Text;

namespace Mokaterm.Modules.Mqtt;

/// <summary>One message to send. The payload is the bytes as they go on the wire, already encoded.</summary>
public sealed record MqttPublishRequest
{
	public required string Topic { get; init; }

	public ReadOnlyMemory<byte> Payload { get; init; }

	public MqttQos QualityOfService { get; init; }

	/// <summary>
	/// Ask the broker to keep this message as the topic's current value. An empty payload with this set is how a
	/// retained message is cleared.
	/// </summary>
	public bool Retain { get; init; }

	/// <summary>A request carrying <paramref name="text"/> as UTF-8, which is what the publish form sends.</summary>
	public static MqttPublishRequest FromText(string topic, string? text, MqttQos qos, bool retain) => new()
	{
		Topic = topic,
		Payload = string.IsNullOrEmpty(text) ? ReadOnlyMemory<byte>.Empty : Encoding.UTF8.GetBytes(text),
		QualityOfService = qos,
		Retain = retain,
	};

	/// <summary>Null when the request can go to a broker, otherwise why it cannot.</summary>
	public string? Validate() => MqttTopicFilters.ValidateTopic(Topic);
}
