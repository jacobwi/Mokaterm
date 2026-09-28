namespace Mokaterm.Modules.Mqtt.Messages;

/// <summary>
/// A message a topic node holds, together with its place in the store's age order. Keeping the order node here is
/// what makes evicting the oldest message of one topic as cheap as evicting the oldest of the session.
/// </summary>
internal sealed class MqttKeptMessage
{
	public MqttKeptMessage(MqttMessage message) => Message = message;

	public MqttMessage Message { get; }

	/// <summary>This message's entry in the store's global age list, set right after it is added.</summary>
	public LinkedListNode<MqttKeptMessage>? Order { get; set; }
}
