using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Modules;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Modules.Vnc.Components;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Icons;

namespace Mokaterm.Modules.Vnc.Tests;

public sealed class VncModuleTests
{
	[Fact]
	public void Info_DescribesTheModule()
	{
		ModuleInfo info = new VncModule().Info;

		Assert.Equal("vnc", info.Id);
		Assert.Equal("VNC", info.Name);
		Assert.False(string.IsNullOrWhiteSpace(info.Version));
		Assert.DoesNotContain('+', info.Version);
	}

	[Fact]
	public void AddVnc_RegistersProviderViewEditorAndSettingsPage()
	{
		TestBuilder builder = new();

		builder.AddVnc();

		ServiceDescriptor provider = Assert.Single(builder.Services, descriptor => descriptor.ServiceType == typeof(IProtocolProvider));
		Assert.Equal(ServiceLifetime.Singleton, provider.Lifetime);
		Assert.Equal(typeof(VncProtocolProvider), provider.ImplementationType);

		ProtocolUiDescriptor ui = Assert.Single(builder.Services.Registered<ProtocolUiDescriptor>());
		Assert.Equal("vnc", ui.ProtocolId);
		Assert.Equal(MokatermIcons.Monitor, ui.Icon);
		Assert.Equal(typeof(VncOptionsEditor), ui.OptionsEditor);
		Assert.Equal(typeof(VncSessionView), ui.SessionView);

		SettingsPageDescriptor page = Assert.Single(builder.Services.Registered<SettingsPageDescriptor>());
		Assert.Equal("vnc", page.Id);
		Assert.Equal("VNC", page.Title);
		Assert.Equal(SettingsPageGroup.Protocols, page.Group);
		Assert.Equal(typeof(VncSettingsPage), page.Component);
	}

	[Fact]
	public void ConfigureServices_Twice_RegistersTheProviderOnce()
	{
		ServiceCollection services = new();
		VncModule module = new();

		module.ConfigureServices(services);
		module.ConfigureServices(services);

		Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IProtocolProvider));
	}

	[Fact]
	public void Provider_ResolvesFromContainer_WithTheVncDescriptor()
	{
		TestBuilder builder = new();
		builder.Services.AddSingleton<ISettingsService, FakeSettingsService>();
		builder.Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);

		// The module's view services need the browser, which every host has and no test does.
		builder.Services.AddScoped<IJSRuntime, FakeJsRuntime>();
		builder.AddVnc();
		using ServiceProvider services = builder.Services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

		ProtocolDescriptor descriptor = Assert.Single(services.GetServices<IProtocolProvider>()).Descriptor;

		Assert.Equal("vnc", descriptor.Id);
		Assert.Equal("VNC", descriptor.DisplayName);
		Assert.Equal(5900, descriptor.DefaultPort);
		Assert.Equal(ProtocolCapabilities.RemoteDesktop, descriptor.Capabilities);
		Assert.Equal([AuthenticationMethod.Password, AuthenticationMethod.Anonymous], descriptor.AuthenticationMethods);
		Assert.False(descriptor.RequiresUsername);
		Assert.Null(descriptor.VariantOf);
	}
}
