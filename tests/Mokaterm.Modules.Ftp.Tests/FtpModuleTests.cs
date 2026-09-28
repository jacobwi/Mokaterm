using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Modules;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Modules.Ftp.Components;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Icons;

namespace Mokaterm.Modules.Ftp.Tests;

public sealed class FtpModuleTests
{
	[Fact]
	public void Info_DescribesTheModule()
	{
		ModuleInfo info = new FtpModule().Info;

		Assert.Equal("ftp", info.Id);
		Assert.Equal("FTP & FTPS", info.Name);
		Assert.False(string.IsNullOrWhiteSpace(info.Version));
		Assert.DoesNotContain('+', info.Version);
	}

	[Fact]
	public void AddFtp_RegistersProviderEditorAndSettingsPage()
	{
		TestBuilder builder = new();

		builder.AddFtp();

		ServiceDescriptor provider = Assert.Single(builder.Services, descriptor => descriptor.ServiceType == typeof(IProtocolProvider));
		Assert.Equal(ServiceLifetime.Singleton, provider.Lifetime);
		Assert.Equal(typeof(FtpProtocolProvider), provider.ImplementationType);

		ProtocolUiDescriptor ui = Assert.Single(builder.Services.Registered<ProtocolUiDescriptor>());
		Assert.Equal("ftp", ui.ProtocolId);
		Assert.Equal(MokatermIcons.Globe, ui.Icon);
		Assert.Equal(typeof(FtpOptionsEditor), ui.OptionsEditor);
		Assert.Null(ui.SessionView);

		SettingsPageDescriptor page = Assert.Single(builder.Services.Registered<SettingsPageDescriptor>());
		Assert.Equal("ftp", page.Id);
		Assert.Equal("FTP", page.Title);
		Assert.Equal(SettingsPageGroup.Protocols, page.Group);
		Assert.Equal(MokatermIcons.Globe, page.Icon);
		Assert.Equal(typeof(FtpSettingsPage), page.Component);
		Assert.Equal("ftps tls passive timeout", page.Keywords);
	}

	[Fact]
	public void ConfigureServices_Twice_RegistersTheProviderOnce()
	{
		ServiceCollection services = new();
		FtpModule module = new();

		module.ConfigureServices(services);
		module.ConfigureServices(services);

		Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IProtocolProvider));
	}

	[Fact]
	public void Provider_ResolvesFromContainer_WithTheFtpDescriptor()
	{
		TestBuilder builder = new();
		builder.Services.AddSingleton<ISettingsService, FakeSettingsService>();
		builder.Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
		builder.AddFtp();
		using ServiceProvider services = builder.Services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

		ProtocolDescriptor descriptor = Assert.Single(services.GetServices<IProtocolProvider>()).Descriptor;

		Assert.Equal("ftp", descriptor.Id);
		Assert.Equal("FTP", descriptor.DisplayName);
		Assert.Equal(21, descriptor.DefaultPort);
		Assert.Equal(ProtocolCapabilities.FileSystem, descriptor.Capabilities);
		Assert.Equal([AuthenticationMethod.Password, AuthenticationMethod.Anonymous], descriptor.AuthenticationMethods);
		Assert.False(descriptor.RequiresUsername);
		Assert.Equal(10, descriptor.Order);
		Assert.Null(descriptor.VariantOf);
	}
}
