using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Modules;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Modules.Local.Components;
using Mokaterm.Modules.Local.Containers;
using Mokaterm.UI.Common.Contributions;

namespace Mokaterm.Modules.Local.Tests;

public sealed class LocalModuleTests
{
	[Fact]
	public void Info_DescribesTheModule()
	{
		ModuleInfo info = new LocalModule().Info;

		Assert.Equal("local", info.Id);
		Assert.Equal("Local shell", info.Name);
		Assert.False(string.IsNullOrWhiteSpace(info.Version));
		Assert.DoesNotContain('+', info.Version);
	}

	[Fact]
	public void AddLocal_RegistersProvidersEditorsAndSettingsPage()
	{
		TestBuilder builder = new();

		builder.AddLocal();

		ServiceDescriptor[] providers = [.. builder.Services.Where(descriptor => descriptor.ServiceType == typeof(IProtocolProvider))];
		Assert.All(providers, provider => Assert.Equal(ServiceLifetime.Singleton, provider.Lifetime));
		Assert.Equal(
			[typeof(LocalProtocolProvider), typeof(DockerShellProvider), typeof(KubernetesShellProvider)],
			providers.Select(provider => provider.ImplementationType));

		ProtocolUiDescriptor[] uis = [.. builder.Services.Registered<ProtocolUiDescriptor>()];
		Assert.Equal(["local", "docker", "kubernetes"], uis.Select(ui => ui.ProtocolId));
		Assert.Equal([LocalIcons.Shell, LocalIcons.Container, LocalIcons.Pod], uis.Select(ui => ui.Icon));
		Assert.Equal(
			[typeof(LocalOptionsEditor), typeof(DockerShellOptionsEditor), typeof(KubernetesShellOptionsEditor)],
			uis.Select(ui => ui.OptionsEditor));

		// Sessions expose a terminal channel, so the shell's own terminal view shows them.
		Assert.All(uis, ui => Assert.Null(ui.SessionView));
		Assert.Empty(builder.Services.Registered<SessionToolDescriptor>());

		SettingsPageDescriptor page = Assert.Single(builder.Services.Registered<SettingsPageDescriptor>());
		Assert.Equal("local", page.Id);
		Assert.Equal("Local shell", page.Title);
		Assert.Equal(SettingsPageGroup.Protocols, page.Group);
		Assert.Equal(typeof(LocalSettingsPage), page.Component);
	}

	[Fact]
	public void ConfigureServices_Twice_RegistersEachProviderOnce()
	{
		ServiceCollection services = new();
		LocalModule module = new();

		module.ConfigureServices(services);
		module.ConfigureServices(services);

		Assert.Equal(3, services.Count(descriptor => descriptor.ServiceType == typeof(IProtocolProvider)));
	}

	[Fact]
	public void Provider_ResolvesFromContainer_WithTheLocalDescriptor()
	{
		TestBuilder builder = new();
		builder.Services.AddSingleton<ISettingsService, FakeSettingsService>();
		builder.Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);

		// The shared UI services the module pulls in need the browser, which every host has and no test does.
		builder.Services.AddScoped<IJSRuntime, FakeJsRuntime>();
		builder.AddLocal();
		using ServiceProvider services = builder.Services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

		ProtocolDescriptor descriptor = services.GetServices<IProtocolProvider>().Single(provider => provider.Descriptor.Id == "local").Descriptor;

		Assert.Equal("local", descriptor.Id);
		Assert.Equal("Local shell", descriptor.DisplayName);
		Assert.Equal(ProtocolCapabilities.Terminal, descriptor.Capabilities);

		// A shell on this computer has no network port: the machine's address names the shell.
		Assert.False(descriptor.UsesPort);
		Assert.Equal(0, descriptor.DefaultPort);

		// The shell runs as the user running the app; there is nobody to log in as.
		Assert.Equal([AuthenticationMethod.Anonymous], descriptor.AuthenticationMethods);
		Assert.False(descriptor.RequiresUsername);
		Assert.Null(descriptor.VariantOf);
	}

	[Fact]
	public void ContainerProviders_ResolveFromContainer_AsTerminalsWithoutAPort()
	{
		TestBuilder builder = new();
		builder.Services.AddSingleton<ISettingsService, FakeSettingsService>();
		builder.Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
		builder.Services.AddScoped<IJSRuntime, FakeJsRuntime>();
		builder.AddLocal();
		using ServiceProvider services = builder.Services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

		ProtocolDescriptor[] descriptors = [.. services.GetServices<IProtocolProvider>().Select(provider => provider.Descriptor).Where(descriptor => descriptor.Id != "local")];

		Assert.Equal(["docker", "kubernetes"], descriptors.Select(descriptor => descriptor.Id));
		Assert.Equal(["Docker container", "Kubernetes pod"], descriptors.Select(descriptor => descriptor.DisplayName));
		Assert.All(descriptors, descriptor =>
		{
			Assert.Equal(ProtocolCapabilities.Terminal, descriptor.Capabilities);
			Assert.False(descriptor.UsesPort);
			Assert.Equal([AuthenticationMethod.Anonymous], descriptor.AuthenticationMethods);
			Assert.False(descriptor.RequiresUsername);
			Assert.Null(descriptor.VariantOf);
		});
	}
}
