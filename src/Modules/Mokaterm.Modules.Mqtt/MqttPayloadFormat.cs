namespace Mokaterm.Modules.Mqtt;

/// <summary>How the payload viewer reads a message's bytes.</summary>
public enum MqttPayloadFormat
{
	/// <summary>UTF-8 text, with a replacement character wherever the bytes are not.</summary>
	Text,

	/// <summary>Indented JSON. Falls back to text for a payload that is not JSON.</summary>
	Json,

	/// <summary>A hex dump with offsets, for a payload that is not text at all.</summary>
	Hex,
}
