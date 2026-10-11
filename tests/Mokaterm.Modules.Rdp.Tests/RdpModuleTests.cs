using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Modules;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Modules.Rdp.Components;
using Mokaterm.UI.Common.Contributions;

namespace Mokaterm.Modules.Rdp.Tests;

public sealed class RdpModuleTests
{
	[Fact]
	public void Info_DescribesTheModule()
	{
		ModuleInfo info = new RdpModule().Info;

		Assert.Equal("rdp", info.Id);
		Assert.Equal("RDP", info.Name);
		Assert.False(string.IsNullOrWhiteSpace(info.Version));
		Assert.DoesNotContain('+', info.Version);
	}

	[Fact]
	public void AddRdp_RegistersProviderViewEditorAndSettingsPage()
	{
		TestBuilder builder = new();

		builder.AddRdp();

		ServiceDescriptor provider = Assert.Single(builder.Services, descriptor => descriptor.ServiceType == typeof(IProtocolProvider));
		Assert.Equal(ServiceLifetime.Singleton, provider.Lifetime);
		Assert.Equal(typeof(RdpProtocolProvider), provider.ImplementationType);

		ProtocolUiDescriptor ui = Assert.Single(builder.Services.Registered<ProtocolUiDescriptor>());
		Assert.Equal("rdp", ui.ProtocolId);
		Assert.Equal(RdpIcons.RemoteDesktop, ui.Icon);
		Assert.Equal(typeof(RdpOptionsEditor), ui.OptionsEditor);
		Assert.Equal(typeof(RdpSessionView), ui.SessionView);

		SettingsPageDescriptor page = Assert.Single(builder.Services.Registered<SettingsPageDescriptor>());
		Assert.Equal("rdp", page.Id);
		Assert.Equal("RDP", page.Title);
		Assert.Equal(SettingsPageGroup.Protocols, page.Group);
		Assert.Equal(typeof(RdpSettingsPage), page.Component);
	}

	[Fact]
	public void ConfigureServices_Twice_RegistersTheProviderOnce()
	{
		ServiceCollection services = new();
		RdpModule module = new();

		module.ConfigureServices(services);
		module.ConfigureServices(services);

		Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IProtocolProvider));
	}

	[Fact]
	public void Provider_ResolvesFromContainer_WithTheRdpDescriptor()
	{
		TestBuilder builder = new();
		builder.Services.AddSingleton<ISettingsService, FakeSettingsService>();
		builder.Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);

		// The module's view services need the browser, which every host has and no test does.
		builder.Services.AddScoped<IJSRuntime, FakeJsRuntime>();
		builder.AddRdp();
		using ServiceProvider services = builder.Services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

		ProtocolDescriptor descriptor = Assert.Single(services.GetServices<IProtocolProvider>()).Descriptor;

		Assert.Equal("rdp", descriptor.Id);
		Assert.Equal("RDP", descriptor.DisplayName);
		Assert.Equal(3389, descriptor.DefaultPort);
		Assert.Equal(ProtocolCapabilities.RemoteDesktop, descriptor.Capabilities);
		Assert.Equal([AuthenticationMethod.Password, AuthenticationMethod.Anonymous], descriptor.AuthenticationMethods);
		Assert.True(descriptor.RequiresUsername);
		Assert.Null(descriptor.VariantOf);
	}

	[Fact]
	public void AddRdp_RejectsNull() => Assert.Throws<ArgumentNullException>(() => ((IMokatermBuilder)null!).AddRdp());
}
