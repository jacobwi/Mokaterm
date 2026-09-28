using System.Security.Cryptography;

namespace Mokaterm.Modules.Mqtt.Protocol;

/// <summary>Names a connection gives the broker when the login does not name one itself.</summary>
internal static class MqttClientIds
{
	/// <summary>
	/// <c>mokaterm-</c> and eight random hex characters. An empty CONNECT client id would let the broker pick, but
	/// MQTT 3.1.1 only allows that with a clean session and the name it picks says nothing in a broker's client
	/// list, so one is always sent. Two sessions must never share one: a broker takes the older connection down.
	/// </summary>
	public static string Create() => "mokaterm-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));
}
