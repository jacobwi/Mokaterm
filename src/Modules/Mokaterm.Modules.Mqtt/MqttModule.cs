using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mokaterm.Abstractions.Modules;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Mqtt.Components;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Extensions;

namespace Mokaterm.Modules.Mqtt;

/// <summary>MQTT: the <c>mqtt</c> protocol with its explorer, connection options editor and settings page.</summary>
public sealed class MqttModule : IMokatermModule
{
	public const string ModuleId = "mqtt";

	public ModuleInfo Info { get; } = new(
		ModuleId,
		"MQTT",
		"A message broker client: topics, payloads and publishing, over TCP, TLS or WebSockets.",
		DisplayVersion.Of(typeof(MqttModule).Assembly));

	public void ConfigureServices(IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);

		services.AddMokatermUiCommon();
		services.TryAddEnumerable(ServiceDescriptor.Singleton<IProtocolProvider, MqttProtocolProvider>());

		// A broker session has neither a terminal nor a file system, so the module brings the whole view.
		services.AddProtocolUi(new ProtocolUiDescriptor
		{
			ProtocolId = MqttProtocolProvider.ProtocolId,
			Icon = MqttIcons.Broker,
			OptionsEditor = typeof(MqttOptionsEditor),
			SessionView = typeof(MqttSessionView),
		});

		services.AddSettingsPage(new SettingsPageDescriptor
		{
			Id = "mqtt",
			Title = "MQTT",
			Description = "How much of a broker's traffic one session keeps, and what a new publish starts at.",
			Group = SettingsPageGroup.Protocols,
			Icon = MqttIcons.Broker,
			Component = typeof(MqttSettingsPage),
			Keywords = "broker topic subscribe publish retained payload qos websocket iot",
		});
	}
}
