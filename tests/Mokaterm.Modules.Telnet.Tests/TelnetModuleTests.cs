using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Modules;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Modules.Telnet.Components;
using Mokaterm.UI.Common.Contributions;

namespace Mokaterm.Modules.Telnet.Tests;

public sealed class TelnetModuleTests
{
	[Fact]
	public void Info_DescribesTheModule()
	{
		ModuleInfo info = new TelnetModule().Info;

		Assert.Equal("telnet", info.Id);
		Assert.Equal("Telnet", info.Name);
		Assert.False(string.IsNullOrWhiteSpace(info.Version));
		Assert.DoesNotContain('+', info.Version);
	}

	[Fact]
	public void AddTelnet_RegistersProviderEditorAndSettingsPage()
	{
		TestBuilder builder = new();

		builder.AddTelnet();

		ServiceDescriptor provider = Assert.Single(builder.Services, descriptor => descriptor.ServiceType == typeof(IProtocolProvider));
		Assert.Equal(ServiceLifetime.Singleton, provider.Lifetime);
		Assert.Equal(typeof(TelnetProtocolProvider), provider.ImplementationType);

		ProtocolUiDescriptor ui = Assert.Single(builder.Services.Registered<ProtocolUiDescriptor>());
		Assert.Equal("telnet", ui.ProtocolId);
		Assert.Equal(TelnetIcons.Telnet, ui.Icon);
		Assert.Equal(typeof(TelnetOptionsEditor), ui.OptionsEditor);

		// Sessions expose a terminal channel, so the shell's own terminal view shows them.
		Assert.Null(ui.SessionView);

		SettingsPageDescriptor page = Assert.Single(builder.Services.Registered<SettingsPageDescriptor>());
		Assert.Equal("telnet", page.Id);
		Assert.Equal("Telnet", page.Title);
		Assert.Equal(SettingsPageGroup.Protocols, page.Group);
		Assert.Equal(typeof(TelnetSettingsPage), page.Component);
	}

	[Fact]
	public void ConfigureServices_Twice_RegistersTheProviderOnce()
	{
		ServiceCollection services = new();
		TelnetModule module = new();

		module.ConfigureServices(services);
		module.ConfigureServices(services);

		Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IProtocolProvider));
	}

	[Fact]
	public void Provider_ResolvesFromContainer_WithTheTelnetDescriptor()
	{
		TestBuilder builder = new();
		builder.Services.AddSingleton<ISettingsService, FakeSettingsService>();
		builder.Services.AddSingleton(TimeProvider.System);
		builder.Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);

		// The shared UI services the module pulls in need the browser, which every host has and no test does.
		builder.Services.AddScoped<IJSRuntime, FakeJsRuntime>();
		builder.AddTelnet();
		using ServiceProvider services = builder.Services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

		ProtocolDescriptor descriptor = Assert.Single(services.GetServices<IProtocolProvider>()).Descriptor;

		Assert.Equal("telnet", descriptor.Id);
		Assert.Equal("Telnet", descriptor.DisplayName);
		Assert.Equal(23, descriptor.DefaultPort);
		Assert.Equal(ProtocolCapabilities.Terminal, descriptor.Capabilities);

		// Telnet has no login of its own, so a connection needs neither a user name nor a credential.
		Assert.Equal(AuthenticationMethod.Anonymous, descriptor.AuthenticationMethods[0]);
		Assert.Equal([AuthenticationMethod.Anonymous, AuthenticationMethod.Password], descriptor.AuthenticationMethods);
		Assert.False(descriptor.RequiresUsername);
		Assert.Null(descriptor.VariantOf);
	}
}
