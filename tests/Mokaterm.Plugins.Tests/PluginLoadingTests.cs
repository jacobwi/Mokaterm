using System.Runtime.Loader;
using Microsoft.Extensions.DependencyInjection;
using Mokaterm.Abstractions.Modules;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Plugins;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Core.Extensions;
using Mokaterm.Core.Plugins;
using Mokaterm.UI.Common.Components;
using Mokaterm.UI.Common.Contributions;

namespace Mokaterm.Plugins.Tests;

/// <summary>
/// The sample package through the whole life a user gives it: read, installed and trusted through the manager, applied
/// at the next start, loaded into a context of its own, and registered like a built-in module. Only public types are
/// used, the same ones a host uses.
/// </summary>
public sealed class PluginLoadingTests : IDisposable
{
	private readonly string _folder = Path.Combine(Path.GetTempPath(), "mokaterm-plugin-tests", Guid.NewGuid().ToString("N"));

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task SamplePackage_InstalledAndTrusted_LoadsAtTheNextStart()
	{
		PluginCatalog first = Open();
		first.ConfirmStarted();
		using (ServiceProvider services = Services(first))
		{
			IPluginManager manager = services.GetRequiredService<IPluginManager>();
			await using FileStream file = File.OpenRead(SamplePluginPackage.FilePath);
			using IPluginPackage package = await manager.ReadPackageAsync(file, Ct);

			Assert.Equal(SamplePluginPackage.Id, package.Id);
			Assert.True(package.HasCode);
			Assert.Null(package.Problem);
			Assert.Equal(1, package.ThemeCount);

			await manager.InstallAsync(package, enable: true, Ct);
			Assert.True(manager.RestartRequired);
		}

		PluginCatalog next = Open();
		next.ConfirmStarted();

		PluginRecord record = Assert.Single(next.Records);
		Assert.Equal(PluginStatus.Enabled, record.Status);
		LoadedPlugin loaded = Assert.Single(next.Loaded);
		Assert.Equal(SamplePluginPackage.AssemblyName, loaded.Info.AssemblyName);
		Assert.Equal($"_content/{SamplePluginPackage.AssemblyName}/{SamplePluginPackage.AssemblyName}.bundle.scp.css", loaded.Info.StyleSheet);
		Assert.True(File.Exists(Path.Combine(loaded.Info.WebRoot!, "js", "sample.js")));
		Assert.Single(next.Records, plugin => plugin.ThemeCount == 1);
	}

	[Fact]
	public async Task LoadedPlugin_SharesTheHostsTypes_AndRegistersLikeABuiltInModule()
	{
		await InstallTrustedAsync();
		PluginCatalog catalog = Open();
		catalog.ConfirmStarted();

		using ServiceProvider services = Services(catalog);

		// The module came from the plugin's own context...
		Type moduleType = Assert.Single(catalog.Loaded).Module.GetType();
		Assert.NotSame(AssemblyLoadContext.Default, AssemblyLoadContext.GetLoadContext(moduleType.Assembly));

		// ...while everything it builds on is the host's, or the shell could not use what it registers.
		Assert.Contains(services.GetServices<ModuleInfo>(), info => info.Id == "sample-plugin");
		SessionToolDescriptor tool = Assert.Single(services.GetServices<SessionToolDescriptor>(), tool => tool.Key == "sample-plugin" && tool.ProtocolId == "demo");
		Assert.True(typeof(SessionViewBase).IsAssignableFrom(tool.Component));
		Assert.Same(typeof(SessionViewBase).Assembly, tool.Component.BaseType!.Assembly);
		Assert.Contains(services.GetServices<PluginInfo>(), info => info.Id == SamplePluginPackage.Id);
	}

	[Fact]
	public async Task PluginWhoseModuleIdIsTaken_IsRefusedOutLoud()
	{
		await InstallTrustedAsync();
		PluginCatalog catalog = Open();
		catalog.ConfirmStarted();

		// A built-in module already holds the sample's module id.
		ServiceCollection services = new();
		new MokatermBuilder(services).AddModule(new TakenModule()).AddPlugins(catalog);
		using ServiceProvider provider = services.BuildServiceProvider();

		PluginRecord record = Assert.Single(provider.GetRequiredService<IPluginManager>().Plugins);
		Assert.Equal(PluginStatus.Failed, record.Status);
		Assert.Contains("sample-plugin", record.Problem, StringComparison.Ordinal);
		Assert.Empty(provider.GetServices<PluginInfo>());
		Assert.DoesNotContain(provider.GetServices<SessionToolDescriptor>(), tool => tool.Key == "sample-plugin");
	}

	[Fact]
	public async Task LoadedPlugin_BringsItsSettingsPage_UnderItsOwnName()
	{
		await InstallTrustedAsync();
		PluginCatalog catalog = Open();
		catalog.ConfirmStarted();

		using ServiceProvider services = Services(catalog);

		SettingsPageDescriptor page = Assert.Single(services.GetServices<SettingsPageDescriptor>());
		Assert.Equal("plugin.moka.sample", page.Id);
		Assert.Equal(SettingsPageGroup.Plugins, page.Group);
		Assert.Equal(SamplePluginPackage.AssemblyName, page.Component.Assembly.GetName().Name);
		SettingsSectionType section = Assert.Single(services.GetServices<SettingsSectionType>());
		Assert.Equal("plugin.moka.sample", section.Key);
		Assert.Equal(PluginStatus.Enabled, Assert.Single(services.GetRequiredService<IPluginManager>().Plugins).Status);
	}

	[Fact]
	public async Task PluginWhoseSettingsKeyIsTaken_HasEverythingItAddedTakenBackOut()
	{
		await InstallTrustedAsync();
		PluginCatalog catalog = Open();
		catalog.ConfirmStarted();

		// Something else already stores another type under the sample's key.
		ServiceCollection services = new();
		services.AddSingleton(SettingsSectionType.Of<SomeoneElsesSettings>());
		new MokatermBuilder(services).AddPlugins(catalog);
		using ServiceProvider provider = services.BuildServiceProvider();

		PluginRecord record = Assert.Single(provider.GetRequiredService<IPluginManager>().Plugins);
		Assert.Equal(PluginStatus.Failed, record.Status);
		Assert.Contains("plugin.moka.sample", record.Problem, StringComparison.Ordinal);
		Assert.Empty(provider.GetServices<SettingsPageDescriptor>());
		Assert.Empty(provider.GetServices<SessionToolDescriptor>());
		Assert.DoesNotContain(provider.GetServices<ModuleInfo>(), info => info.Id == "sample-plugin");
		Assert.Empty(provider.GetServices<PluginInfo>());
		Assert.Same(typeof(SomeoneElsesSettings), Assert.Single(provider.GetServices<SettingsSectionType>()).Type);
	}

	[Fact]
	public void OperatorFolder_OnTheWebHost_LoadsWithoutAnyTrustStep()
	{
		// What an operator does: unzip the package into the server's plugins folder.
		System.IO.Compression.ZipFile.ExtractToDirectory(SamplePluginPackage.FilePath, Path.Combine(_folder, SamplePluginPackage.Id));

		PluginCatalog catalog = PluginCatalog.Open(new PluginCatalogOptions { Directory = _folder, Host = HostKind.Web });

		Assert.Equal(PluginStatus.Enabled, Assert.Single(catalog.Records).Status);
		Assert.Single(catalog.Loaded);
	}

	public void Dispose()
	{
		try
		{
			// The assembly stays loaded (contexts are not collectible), so its file may still be held; a leftover temp
			// folder fails nothing.
			Directory.Delete(_folder, recursive: true);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
		}
	}

	private sealed record SomeoneElsesSettings : ISettingsSection
	{
		public static string SectionKey => "plugin.moka.sample";
	}

	private sealed class TakenModule : IMokatermModule
	{
		public ModuleInfo Info { get; } = new("sample-plugin", "Built in", "Holds the id first.", "1.0.0");

		public void ConfigureServices(IServiceCollection services)
		{
		}
	}

	private PluginCatalog Open() => PluginCatalog.Open(new PluginCatalogOptions { Directory = _folder, Host = HostKind.Desktop, UserManaged = true });

	private static ServiceProvider Services(PluginCatalog catalog)
	{
		ServiceCollection services = new();
		new MokatermBuilder(services).AddPlugins(catalog);
		return services.BuildServiceProvider();
	}

	private async Task InstallTrustedAsync()
	{
		PluginCatalog catalog = Open();
		catalog.ConfirmStarted();
		using ServiceProvider services = Services(catalog);
		IPluginManager manager = services.GetRequiredService<IPluginManager>();
		await using FileStream file = File.OpenRead(SamplePluginPackage.FilePath);
		using IPluginPackage package = await manager.ReadPackageAsync(file, Ct);
		await manager.InstallAsync(package, enable: true, Ct);
	}
}
