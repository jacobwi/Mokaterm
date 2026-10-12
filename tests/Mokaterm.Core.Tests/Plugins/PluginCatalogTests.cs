using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Plugins;
using Mokaterm.Core.Plugins;

namespace Mokaterm.Core.Tests.Plugins;

public sealed class PluginCatalogTests
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Install_WaitsForTheNextStart_ThenTheThemePackIsOn()
	{
		using TempPluginsFolder temp = new();
		using (PluginManager manager = new(OpenStarted(temp)))
		{
			using IPluginPackage package = await ReadAsync(manager, TestPlugins.ThemePack("moka.themes"));
			await manager.InstallAsync(package, enable: true, Ct);

			PluginRecord waiting = Assert.Single(manager.Plugins);
			Assert.Equal(PluginPendingChange.Install, waiting.Pending);
			Assert.True(manager.RestartRequired);
		}

		PluginCatalog next = temp.Open();
		next.ConfirmStarted();

		PluginRecord record = Assert.Single(next.Records);
		Assert.Equal(PluginStatus.Enabled, record.Status);
		Assert.Equal(1, record.ThemeCount);
		Assert.Single(next.Themes);
		Assert.Empty(next.Loaded);
		Assert.True(Directory.Exists(Path.Combine(temp.Path, "moka.themes")));
		Assert.False(Directory.Exists(Path.Combine(temp.Path, ".pending", "moka.themes")));
	}

	[Fact]
	public async Task CodePlugin_InstalledOff_StaysOff()
	{
		using TempPluginsFolder temp = new();
		await InstallAsync(temp, TestPlugins.CodePlugin("moka.code"), enable: false);

		PluginRecord record = Assert.Single(temp.Open().Records);

		Assert.Equal(PluginStatus.Disabled, record.Status);
		Assert.True(record.HasCode);
		Assert.False(record.IsEnabled);
	}

	[Fact]
	public async Task CodePlugin_TrustedFiles_AreLoaded_AndAFailureIsARecordNotACrash()
	{
		using TempPluginsFolder temp = new();
		await InstallAsync(temp, TestPlugins.CodePlugin("moka.code"), enable: true);

		PluginCatalog catalog = temp.Open();

		// The trusted files were loaded; they are not a real assembly, so loading fails and says so.
		PluginRecord record = Assert.Single(catalog.Records);
		Assert.Equal(PluginStatus.Failed, record.Status);
		Assert.NotNull(record.Problem);
		Assert.Empty(catalog.Loaded);
	}

	[Fact]
	public async Task CodePlugin_FilesChangedAfterTrust_IsNotLoaded()
	{
		using TempPluginsFolder temp = new();
		await InstallAsync(temp, TestPlugins.CodePlugin("moka.code"), enable: true);
		temp.Open().ConfirmStarted();

		File.WriteAllText(Path.Combine(temp.Path, "moka.code", "lib", "moka.code.dll"), "swapped");
		PluginRecord record = Assert.Single(temp.Open().Records);

		Assert.Equal(PluginStatus.Changed, record.Status);
	}

	[Fact]
	public async Task TrustingAgain_LoadsTheNewFilesAtTheNextStart()
	{
		using TempPluginsFolder temp = new();
		await InstallAsync(temp, TestPlugins.CodePlugin("moka.code"), enable: true);
		temp.Open().ConfirmStarted();
		File.WriteAllText(Path.Combine(temp.Path, "moka.code", "lib", "moka.code.dll"), "swapped");

		PluginCatalog changed = temp.Open();
		changed.ConfirmStarted();
		using (PluginManager manager = new(changed))
		{
			PluginFileSet files = await manager.ReadFilesAsync("moka.code", Ct);
			Assert.Contains(files.Files, file => file.Path == "lib/moka.code.dll");
			await manager.SetEnabledAsync("moka.code", enabled: true, files.Hash, Ct);
			Assert.Equal(PluginPendingChange.Enable, Assert.Single(manager.Plugins).Pending);
		}

		// Trusted now, so the loader gets as far as the (fake) assembly.
		Assert.Equal(PluginStatus.Failed, Assert.Single(temp.Open().Records).Status);
	}

	[Fact]
	public async Task Trusting_RefusesFilesThatChangedAfterTheyWereShown()
	{
		using TempPluginsFolder temp = new();
		await InstallAsync(temp, TestPlugins.CodePlugin("moka.code"), enable: false);
		using PluginManager manager = new(OpenStarted(temp));
		PluginFileSet shown = await manager.ReadFilesAsync("moka.code", Ct);

		File.WriteAllText(Path.Combine(temp.Path, "moka.code", "lib", "moka.code.dll"), "swapped after the look");

		await Assert.ThrowsAsync<InvalidDataException>(() => manager.SetEnabledAsync("moka.code", enabled: true, shown.Hash, Ct));
		Assert.False(Assert.Single(manager.Plugins).IsEnabled);
	}

	[Fact]
	public void ManualFolderOnTheDesktop_IsOffUntilTrusted()
	{
		using TempPluginsFolder temp = new();
		TestPlugins.WriteFolder(Path.Combine(temp.Path, "moka.code"), TestPlugins.CodePlugin("moka.code"));

		Assert.Equal(PluginStatus.Disabled, Assert.Single(temp.Open().Records).Status);
	}

	[Fact]
	public void OperatorFolder_EverythingInItIsOn()
	{
		using TempPluginsFolder temp = new();
		TestPlugins.WriteFolder(Path.Combine(temp.Path, "moka.themes"), TestPlugins.ThemePack("moka.themes"));

		PluginCatalog catalog = temp.Open(userManaged: false, host: HostKind.Web);
		using PluginManager manager = new(catalog);

		Assert.Equal(PluginStatus.Enabled, Assert.Single(catalog.Records).Status);
		Assert.False(manager.CanInstall);
		Assert.False(File.Exists(Path.Combine(temp.Path, "plugins.json")));
	}

	[Fact]
	public void MissingOperatorFolder_IsSimplyEmpty()
	{
		using TempPluginsFolder temp = new();

		PluginCatalog catalog = temp.Open(userManaged: false, host: HostKind.Web);

		Assert.Empty(catalog.Records);
		Assert.False(Directory.Exists(temp.Path));
	}

	[Fact]
	public void PluginForTheOtherHost_IsIncompatible()
	{
		using TempPluginsFolder temp = new();
		Dictionary<string, string> files = TestPlugins.ThemePack("moka.themes");
		files["plugin.json"] = TestPlugins.Manifest("moka.themes", hosts: "\"desktop\"");
		TestPlugins.WriteFolder(Path.Combine(temp.Path, "moka.themes"), files);

		PluginRecord record = Assert.Single(temp.Open(userManaged: false, host: HostKind.Web).Records);

		Assert.Equal(PluginStatus.Incompatible, record.Status);
		Assert.Equal("This plugin is for the desktop app only.", record.Problem);
	}

	[Fact]
	public void FolderNamedOtherThanTheId_Fails()
	{
		using TempPluginsFolder temp = new();
		TestPlugins.WriteFolder(Path.Combine(temp.Path, "renamed"), TestPlugins.ThemePack("moka.themes"));

		PluginRecord record = Assert.Single(temp.Open(userManaged: false, host: HostKind.Web).Records);

		Assert.Equal(PluginStatus.Failed, record.Status);
		Assert.Equal("renamed", record.Id);
	}

	[Fact]
	public async Task StartThatDiedWhileLoading_SkipsEveryPluginNextTime()
	{
		using TempPluginsFolder temp = new();
		await InstallAsync(temp, TestPlugins.ThemePack("moka.themes"), enable: true);

		// Opened, but never confirmed: the app died before its first page.
		PluginCatalog died = temp.Open();
		Assert.False(died.IsSafeStart);

		PluginCatalog safe = temp.Open();
		Assert.True(safe.IsSafeStart);
		Assert.Equal(PluginStatus.Skipped, Assert.Single(safe.Records).Status);
		Assert.Empty(safe.Themes);
	}

	[Fact]
	public async Task SkipAll_LoadsNothing_AndLeavesTheNextStartAlone()
	{
		using TempPluginsFolder temp = new();
		await InstallAsync(temp, TestPlugins.ThemePack("moka.themes"), enable: true);

		Assert.True(temp.Open(skipAll: true).IsSafeStart);

		PluginCatalog next = temp.Open();
		Assert.False(next.IsSafeStart);
		Assert.Equal(PluginStatus.Enabled, Assert.Single(next.Records).Status);
	}

	[Fact]
	public async Task Remove_DeletesTheFolderAtTheNextStart()
	{
		using TempPluginsFolder temp = new();
		await InstallAsync(temp, TestPlugins.ThemePack("moka.themes"), enable: true);
		PluginCatalog catalog = temp.Open();
		catalog.ConfirmStarted();

		using (PluginManager manager = new(catalog))
		{
			await manager.RemoveAsync("moka.themes", Ct);
			Assert.Equal(PluginPendingChange.Remove, Assert.Single(manager.Plugins).Pending);
		}

		PluginCatalog next = temp.Open();
		Assert.Empty(next.Records);
		Assert.False(Directory.Exists(Path.Combine(temp.Path, "moka.themes")));
	}

	[Fact]
	public async Task Update_ReplacesTheOldVersionAtTheNextStart()
	{
		using TempPluginsFolder temp = new();
		await InstallAsync(temp, TestPlugins.ThemePack("moka.themes", "1.0.0"), enable: true);
		PluginCatalog catalog = temp.Open();
		catalog.ConfirmStarted();

		using (PluginManager manager = new(catalog))
		{
			using IPluginPackage update = await ReadAsync(manager, TestPlugins.ThemePack("moka.themes", "2.0.0"));
			Assert.Equal("1.0.0", update.InstalledVersion);
			await manager.InstallAsync(update, enable: true, Ct);
			Assert.Equal(PluginPendingChange.Update, Assert.Single(manager.Plugins).Pending);
		}

		Assert.Equal("2.0.0", Assert.Single(temp.Open().Records).Version);
	}

	[Fact]
	public async Task RemovingAWaitingInstall_DropsItAtOnce()
	{
		using TempPluginsFolder temp = new();
		using PluginManager manager = new(temp.Open());
		using IPluginPackage package = await ReadAsync(manager, TestPlugins.ThemePack("moka.themes"));
		await manager.InstallAsync(package, enable: true, Ct);

		await manager.RemoveAsync("moka.themes", Ct);

		Assert.Empty(manager.Plugins);
		Assert.False(manager.RestartRequired);
	}

	[Fact]
	public async Task Install_RefusesAPackageWithAProblem()
	{
		using TempPluginsFolder temp = new();
		using PluginManager manager = new(temp.Open());
		using IPluginPackage package = await ReadAsync(manager, TestPlugins.CodePlugin("moka.code", sdk: "99.0"));

		await Assert.ThrowsAsync<InvalidOperationException>(() => manager.InstallAsync(package, enable: true, Ct));
	}

	[Fact]
	public async Task UnreadableState_TurnsCodeOff()
	{
		using TempPluginsFolder temp = new();
		await InstallAsync(temp, TestPlugins.CodePlugin("moka.code"), enable: true);
		temp.Open().ConfirmStarted();

		File.WriteAllText(Path.Combine(temp.Path, "plugins.json"), "{ not json");

		Assert.Equal(PluginStatus.Disabled, Assert.Single(temp.Open().Records).Status);
	}

	private static PluginCatalog OpenStarted(TempPluginsFolder temp)
	{
		PluginCatalog catalog = temp.Open();
		catalog.ConfirmStarted();
		return catalog;
	}

	private static async Task InstallAsync(TempPluginsFolder temp, IReadOnlyDictionary<string, string> files, bool enable)
	{
		using PluginManager manager = new(OpenStarted(temp));
		using IPluginPackage package = await ReadAsync(manager, files);
		await manager.InstallAsync(package, enable, Ct);
	}

	private static async Task<IPluginPackage> ReadAsync(PluginManager manager, IReadOnlyDictionary<string, string> files)
	{
		using MemoryStream stream = new(TestPlugins.Zip(files));
		return await manager.ReadPackageAsync(stream, Ct);
	}
}
