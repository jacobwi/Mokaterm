namespace Mokaterm.Modules.Mqtt;

/// <summary>How the client reaches the broker.</summary>
public enum MqttTransport
{
	/// <summary>A TCP socket carrying MQTT packets, which is what port 1883 and 8883 answer.</summary>
	Tcp,

	/// <summary>MQTT inside WebSocket frames, which is how a broker behind an HTTP reverse proxy is reached.</summary>
	WebSocket,
}
