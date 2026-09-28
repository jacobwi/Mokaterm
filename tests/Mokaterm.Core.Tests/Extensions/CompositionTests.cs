using Microsoft.Extensions.DependencyInjection;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Import;
using Mokaterm.Abstractions.Modules;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Storage;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Core.Extensions;
using Mokaterm.Core.Security;
using Mokaterm.Core.Tests.TestSupport;

namespace Mokaterm.Core.Tests.Extensions;

public sealed class CompositionTests
{
	[Theory]
	[InlineData(typeof(TimeProvider), ServiceLifetime.Singleton)]
	[InlineData(typeof(IAppDataStore), ServiceLifetime.Singleton)]
	[InlineData(typeof(ISettingsService), ServiceLifetime.Singleton)]
	[InlineData(typeof(ITerminalThemeCatalog), ServiceLifetime.Singleton)]
	[InlineData(typeof(IVault), ServiceLifetime.Scoped)]
	[InlineData(typeof(IVaultDataStore), ServiceLifetime.Scoped)]
	[InlineData(typeof(IKnownHostsStore), ServiceLifetime.Scoped)]
	[InlineData(typeof(IHostIdentityVerifier), ServiceLifetime.Scoped)]
	[InlineData(typeof(ICredentialStore), ServiceLifetime.Scoped)]
	[InlineData(typeof(IConnectionRepository), ServiceLifetime.Scoped)]
	[InlineData(typeof(ISshKeyGenerator), ServiceLifetime.Singleton)]
	[InlineData(typeof(IConnectionImporter), ServiceLifetime.Scoped)]
	public void AddMokatermCore_RegistersServiceOnceWithItsLifetime(Type serviceType, ServiceLifetime lifetime)
	{
		ServiceCollection services = new();

		services.AddMokatermCore();
		services.AddMokatermCore();

		ServiceDescriptor descriptor = Assert.Single(services, descriptor => descriptor.ServiceType == serviceType);
		Assert.Equal(lifetime, descriptor.Lifetime);
	}

	[Fact]
	public void AddMokatermCore_KeepsServicesTheHostRegisteredFirst()
	{
		InMemoryAppDataStore store = new();
		ServiceCollection services = new();
		services.AddSingleton<IAppDataStore>(store);

		services.AddMokatermCore();

		Assert.Same(store, Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IAppDataStore)).ImplementationInstance);
	}

	[Fact]
	public async Task Scope_SharesOneVaultAcrossItsInterfacesButNotAcrossScopes()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope first = context.CreateScope();
		await using AsyncServiceScope second = context.CreateScope();

		IVault vault = first.ServiceProvider.GetRequiredService<IVault>();

		Assert.Same(vault, first.ServiceProvider.GetRequiredService<IVaultCipher>());
		Assert.Same(vault, first.ServiceProvider.GetRequiredService<Vault>());
		Assert.NotSame(vault, second.ServiceProvider.GetRequiredService<IVault>());
		Assert.Same(context.Services.GetRequiredService<ISettingsService>(), second.ServiceProvider.GetRequiredService<ISettingsService>());
	}

	[Fact]
	public void AddMokaterm_AddsEachModuleOnceAndPublishesItsInfo()
	{
		TestModule ssh = new("ssh");
		TestModule duplicate = new("SSH");
		TestModule ftp = new("ftp");
		ServiceCollection services = new();

		services.AddMokaterm(builder => builder.AddModule(ssh).AddModule(duplicate));
		IServiceCollection returned = services.AddMokaterm(builder => builder.AddModule(ssh).AddModule(ftp));

		Assert.Same(services, returned);
		Assert.Equal(1, ssh.ConfigureCalls);
		Assert.Equal(0, duplicate.ConfigureCalls);
		Assert.Equal(1, ftp.ConfigureCalls);
		using ServiceProvider provider = services.BuildServiceProvider();
		Assert.Equal("ftp,ssh", string.Join(',', provider.GetServices<ModuleInfo>().Select(info => info.Id).Order(StringComparer.Ordinal)));
		Assert.Equal(2, provider.GetServices<TestModuleMarker>().Count());
		Assert.NotNull(provider.GetService<TimeProvider>());
	}

	[Fact]
	public void AddMokaterm_WithoutConfigure_RegistersCore()
	{
		ServiceCollection services = new();

		services.AddMokaterm();

		Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IVault));
		Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(ModuleInfo));
	}

	[Fact]
	public void AddMokatermCore_RegistersEachImportSourceOnce()
	{
		ServiceCollection services = new();

		services.AddMokatermCore();
		services.AddMokatermCore();

		List<ServiceDescriptor> sources = [.. services.Where(descriptor => descriptor.ServiceType == typeof(IConnectionImportSource))];
		Assert.Equal(4, sources.Count);
		Assert.All(sources, descriptor => Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime));
	}

	/// <summary>
	/// The importer takes an optional protocol registry, which only the session layer registers, so it has to
	/// resolve in a host that has not added one.
	/// </summary>
	[Fact]
	public async Task Scope_ResolvesTheImporterWithoutAProtocolRegistry()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = context.CreateScope();

		IConnectionImporter importer = scope.ServiceProvider.GetRequiredService<IConnectionImporter>();

		Assert.Equal(4, importer.Sources.Count);
	}

	[Fact]
	public void MokatermBuilder_ExposesTheCollection()
	{
		ServiceCollection services = new();

		MokatermBuilder builder = new(services);

		Assert.Same(services, builder.Services);
		Assert.Throws<ArgumentNullException>(() => builder.AddModule(null!));
	}

	public sealed record TestModuleMarker(string ModuleId);

	public sealed class TestModule : IMokatermModule
	{
		public TestModule(string id) => Info = new ModuleInfo(id, id.ToUpperInvariant(), "Test module", "1.0.0");

		public ModuleInfo Info { get; }

		public int ConfigureCalls { get; private set; }

		public void ConfigureServices(IServiceCollection services)
		{
			ConfigureCalls++;
			services.AddSingleton(new TestModuleMarker(Info.Id));
		}
	}
}
