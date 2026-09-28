namespace Mokaterm.Modules.Mqtt;

/// <summary>
/// Where a broker connection stands. There is no reconnecting state: MQTTnet's client does not reconnect on its
/// own, and a session that lost its connection is ended so the shell offers its own reconnect.
/// </summary>
public enum MqttConnectionState
{
	Connected,

	Closed,
}
