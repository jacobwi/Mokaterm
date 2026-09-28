using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Modules;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Modules.Serial.Components;
using Mokaterm.UI.Common.Contributions;

namespace Mokaterm.Modules.Serial.Tests;

public sealed class SerialModuleTests
{
	[Fact]
	public void Info_DescribesTheModule()
	{
		ModuleInfo info = new SerialModule().Info;

		Assert.Equal("serial", info.Id);
		Assert.Equal("Serial", info.Name);
		Assert.False(string.IsNullOrWhiteSpace(info.Version));
		Assert.DoesNotContain('+', info.Version);
	}

	[Fact]
	public void AddSerial_RegistersProviderEditorToolAndSettingsPage()
	{
		TestBuilder builder = new();

		builder.AddSerial();

		ServiceDescriptor provider = Assert.Single(builder.Services, descriptor => descriptor.ServiceType == typeof(IProtocolProvider));
		Assert.Equal(ServiceLifetime.Singleton, provider.Lifetime);
		Assert.Equal(typeof(SerialProtocolProvider), provider.ImplementationType);

		ProtocolUiDescriptor ui = Assert.Single(builder.Services.Registered<ProtocolUiDescriptor>());
		Assert.Equal("serial", ui.ProtocolId);
		Assert.Equal(SerialIcons.Serial, ui.Icon);
		Assert.Equal(typeof(SerialOptionsEditor), ui.OptionsEditor);

		// Sessions expose a terminal channel, so the shell's own terminal view shows them.
		Assert.Null(ui.SessionView);

		SessionToolDescriptor tool = Assert.Single(builder.Services.Registered<SessionToolDescriptor>());
		Assert.Equal("serial", tool.ProtocolId);
		Assert.Equal(typeof(SerialLineView), tool.Component);

		SettingsPageDescriptor page = Assert.Single(builder.Services.Registered<SettingsPageDescriptor>());
		Assert.Equal("serial", page.Id);
		Assert.Equal("Serial", page.Title);
		Assert.Equal(SettingsPageGroup.Protocols, page.Group);
		Assert.Equal(typeof(SerialSettingsPage), page.Component);
	}

	[Fact]
	public void ConfigureServices_Twice_RegistersTheProviderOnce()
	{
		ServiceCollection services = new();
		SerialModule module = new();

		module.ConfigureServices(services);
		module.ConfigureServices(services);

		Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IProtocolProvider));
	}

	[Fact]
	public void Provider_ResolvesFromContainer_WithTheSerialDescriptor()
	{
		TestBuilder builder = new();
		builder.Services.AddSingleton<ISettingsService, FakeSettingsService>();
		builder.Services.AddSingleton(TimeProvider.System);
		builder.Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);

		// The shared UI services the module pulls in need the browser, which every host has and no test does.
		builder.Services.AddScoped<IJSRuntime, FakeJsRuntime>();
		builder.AddSerial();
		using ServiceProvider services = builder.Services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

		ProtocolDescriptor descriptor = Assert.Single(services.GetServices<IProtocolProvider>()).Descriptor;

		Assert.Equal("serial", descriptor.Id);
		Assert.Equal("Serial", descriptor.DisplayName);
		Assert.Equal(ProtocolCapabilities.Terminal, descriptor.Capabilities);

		// A serial line has no network port: the machine's address is the port name.
		Assert.False(descriptor.UsesPort);
		Assert.Equal(0, descriptor.DefaultPort);

		// A port is opened, not logged in to.
		Assert.Equal([AuthenticationMethod.Anonymous], descriptor.AuthenticationMethods);
		Assert.False(descriptor.RequiresUsername);
		Assert.Null(descriptor.VariantOf);
	}
}
