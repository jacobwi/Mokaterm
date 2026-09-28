using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;

namespace Mokaterm.Modules.Mqtt.Protocol;

/// <summary>
/// The one place the module's own enums meet MQTTnet's. Keeping the mapping here means a stored subscription and a
/// settings file never depend on names in the package.
/// </summary>
internal static class MqttWire
{
	public static MqttQualityOfServiceLevel ToWire(MqttQos qos) => qos switch
	{
		MqttQos.AtLeastOnce => MqttQualityOfServiceLevel.AtLeastOnce,
		MqttQos.ExactlyOnce => MqttQualityOfServiceLevel.ExactlyOnce,
		_ => MqttQualityOfServiceLevel.AtMostOnce,
	};

	public static MqttQos FromWire(MqttQualityOfServiceLevel qos) => qos switch
	{
		MqttQualityOfServiceLevel.AtLeastOnce => MqttQos.AtLeastOnce,
		MqttQualityOfServiceLevel.ExactlyOnce => MqttQos.ExactlyOnce,
		_ => MqttQos.AtMostOnce,
	};

	public static MqttProtocolVersion ToWire(MqttProtocolLevel level) => level switch
	{
		MqttProtocolLevel.V311 => MqttProtocolVersion.V311,
		MqttProtocolLevel.V310 => MqttProtocolVersion.V310,
		_ => MqttProtocolVersion.V500,
	};

	/// <summary>
	/// The QoS a SUBACK granted, or null when its code is a refusal. MQTTnet reports both through one enum whose
	/// first three members are the granted levels.
	/// </summary>
	public static MqttQos? Granted(MqttClientSubscribeResultCode code) => code switch
	{
		MqttClientSubscribeResultCode.GrantedQoS0 => MqttQos.AtMostOnce,
		MqttClientSubscribeResultCode.GrantedQoS1 => MqttQos.AtLeastOnce,
		MqttClientSubscribeResultCode.GrantedQoS2 => MqttQos.ExactlyOnce,
		_ => null,
	};
}
