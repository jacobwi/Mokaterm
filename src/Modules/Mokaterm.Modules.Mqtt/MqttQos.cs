namespace Mokaterm.Modules.Mqtt;

/// <summary>
/// What a publisher asks the broker to guarantee. The numbers are the ones on the wire, and this is the module's
/// own enum rather than MQTTnet's so a stored subscription and a settings file keep their meaning across package
/// updates.
/// </summary>
public enum MqttQos
{
	/// <summary>Fire and forget: the message may be lost, never repeated.</summary>
	AtMostOnce = 0,

	/// <summary>Acknowledged, and repeated until it is: the message may arrive twice.</summary>
	AtLeastOnce = 1,

	/// <summary>A four-part handshake per message: exactly one delivery, and the slowest.</summary>
	ExactlyOnce = 2,
}
