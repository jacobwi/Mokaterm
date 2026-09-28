using Mokaterm.Abstractions.Modules;

namespace Mokaterm.Modules.Mqtt;

public static class MqttMokatermBuilderExtensions
{
	/// <summary>Adds the MQTT module: the <c>mqtt</c> protocol with its explorer, options editor and settings page.</summary>
	public static IMokatermBuilder AddMqtt(this IMokatermBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);
		return builder.AddModule(new MqttModule());
	}
}
