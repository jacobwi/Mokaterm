namespace Mokaterm.Modules.Mqtt;

/// <summary>
/// The MQTT version the client offers in its CONNECT. A broker that only speaks an older one refuses 5.0 outright,
/// so this is per connection rather than a setting.
/// </summary>
public enum MqttProtocolLevel
{
	/// <summary>MQTT 5.0: reason codes, user properties and topic aliases. What a current broker speaks.</summary>
	V500,

	/// <summary>MQTT 3.1.1, the version every broker and device supports.</summary>
	V311,

	/// <summary>MQTT 3.1, for devices old enough to need it.</summary>
	V310,
}
