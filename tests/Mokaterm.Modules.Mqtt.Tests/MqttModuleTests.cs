using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Modules;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Modules.Mqtt.Components;
using Mokaterm.UI.Common.Contributions;

namespace Mokaterm.Modules.Mqtt.Tests;

public sealed class MqttModuleTests
{
	[Fact]
	public void Info_DescribesTheModule()
	{
		ModuleInfo info = new MqttModule().Info;

		Assert.Equal("mqtt", info.Id);
		Assert.Equal("MQTT", info.Name);
		Assert.False(string.IsNullOrWhiteSpace(info.Version));
		Assert.DoesNotContain('+', info.Version);
	}

	[Fact]
	public void AddMqtt_RegistersProviderEditorViewAndSettingsPage()
	{
		TestBuilder builder = new();

		builder.AddMqtt();

		ServiceDescriptor provider = Assert.Single(builder.Services, descriptor => descriptor.ServiceType == typeof(IProtocolProvider));
		Assert.Equal(ServiceLifetime.Singleton, provider.Lifetime);
		Assert.Equal(typeof(MqttProtocolProvider), provider.ImplementationType);

		ProtocolUiDescriptor ui = Assert.Single(builder.Services.Registered<ProtocolUiDescriptor>());
		Assert.Equal("mqtt", ui.ProtocolId);
		Assert.Equal(MqttIcons.Broker, ui.Icon);
		Assert.Equal(typeof(MqttOptionsEditor), ui.OptionsEditor);

		// A broker session has neither a terminal nor a file system, so the module brings the view.
		Assert.Equal(typeof(MqttSessionView), ui.SessionView);

		SettingsPageDescriptor page = Assert.Single(builder.Services.Registered<SettingsPageDescriptor>());
		Assert.Equal("mqtt", page.Id);
		Assert.Equal("MQTT", page.Title);
		Assert.Equal(SettingsPageGroup.Protocols, page.Group);
		Assert.Equal(typeof(MqttSettingsPage), page.Component);
	}

	[Fact]
	public void ConfigureServices_Twice_RegistersTheProviderOnce()
	{
		ServiceCollection services = new();
		MqttModule module = new();

		module.ConfigureServices(services);
		module.ConfigureServices(services);

		Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IProtocolProvider));
	}

	[Fact]
	public void Provider_ResolvesFromContainer_WithTheMqttDescriptor()
	{
		TestBuilder builder = new();
		builder.Services.AddSingleton<ISettingsService, FakeSettingsService>();
		builder.Services.AddSingleton(TimeProvider.System);
		builder.Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);

		// The shared UI services the module pulls in need the browser, which every host has and no test does.
		builder.Services.AddScoped<IJSRuntime, FakeJsRuntime>();
		builder.AddMqtt();
		using ServiceProvider services = builder.Services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

		ProtocolDescriptor descriptor = Assert.Single(services.GetServices<IProtocolProvider>()).Descriptor;

		Assert.Equal("mqtt", descriptor.Id);
		Assert.Equal("MQTT", descriptor.DisplayName);
		Assert.Equal(1883, descriptor.DefaultPort);
		Assert.Equal(ProtocolCapabilities.Messaging, descriptor.Capabilities);

		// A broker may take a login or take anyone, and it never takes a key.
		Assert.Equal(AuthenticationMethod.Password, descriptor.AuthenticationMethods[0]);
		Assert.Equal([AuthenticationMethod.Password, AuthenticationMethod.Anonymous], descriptor.AuthenticationMethods);
		Assert.False(descriptor.RequiresUsername);
		Assert.Null(descriptor.VariantOf);
	}
}
