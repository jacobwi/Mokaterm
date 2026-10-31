using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Plugins;
using Mokaterm.Core.Plugins;
using Mokaterm.Core.Plugins.Gallery;

namespace Mokaterm.Core.Tests.Plugins.Gallery;

/// <summary>
/// The gallery as a window uses it: nothing asked until the list is wanted, the list kept until a refresh, and a
/// download that has to match the list in size, hash, id, version and kind before the plugin manager reads it the way
/// it reads a picked file.
/// </summary>
public sealed class PluginGalleryTests : IDisposable
{
	private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

	private readonly TempPluginsFolder _folder = new();
	private readonly PluginManager _manager;

	public PluginGalleryTests()
	{
		_manager = new PluginManager(_folder.Open());
	}

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	public void Dispose()
	{
		_manager.Dispose();
		_folder.Dispose();
	}

	[Fact]
	public async Task NothingIsAsked_UntilTheListIsWanted_AndThenOnlyOnce()
	{
		TestGallery server = new();
		server.Publish(TestGallery.Index());
		using PluginGallery gallery = server.Gallery(_manager);
		int changes = 0;
		gallery.Changed += (_, _) => changes++;

		Assert.Null(gallery.Current);
		Assert.Empty(server.Server.Requests);

		PluginGalleryIndex first = await gallery.ReadIndexAsync(refresh: false, Ct).WaitAsync(Limit, Ct);
		PluginGalleryIndex again = await gallery.ReadIndexAsync(refresh: false, Ct).WaitAsync(Limit, Ct);

		Assert.Same(first, again);
		Assert.Same(first, gallery.Current);
		Assert.Equal(2, server.Server.Requests.Count);
		Assert.Equal(1, changes);

		PluginGalleryIndex refreshed = await gallery.ReadIndexAsync(refresh: true, Ct).WaitAsync(Limit, Ct);

		Assert.NotSame(first, refreshed);
		Assert.Equal(4, server.Server.Requests.Count);
		Assert.Equal(2, changes);
	}

	[Fact]
	public async Task ARefreshThatFails_KeepsTheListAlreadyRead()
	{
		TestGallery server = new();
		server.Publish(TestGallery.Index());
		using PluginGallery gallery = server.Gallery(_manager);
		PluginGalleryIndex first = await gallery.ReadIndexAsync(refresh: false, Ct).WaitAsync(Limit, Ct);
		server.Server.Forget(TestGallery.SignatureAddress);

		await Assert.ThrowsAsync<PluginGalleryException>(() => gallery.ReadIndexAsync(refresh: true, Ct).WaitAsync(Limit, Ct));

		Assert.Same(first, gallery.Current);
	}

	[Fact]
	public async Task Download_OfAMatchingPackage_IsReadWithWhereItCameFrom()
	{
		TestGallery server = new();
		byte[] package = TestPlugins.Zip(TestPlugins.CodePlugin("acme.proxmox", "1.2.0"));
		JsonObject plugin = TestGallery.Plugin("acme.proxmox", hasCode: true, TestGallery.Version("acme.proxmox", "1.2.0", package));
		plugin["verified"] = true;
		server.Publish(TestGallery.Index(plugin));
		server.Host("acme.proxmox", "1.2.0", package);
		using PluginGallery gallery = server.Gallery(_manager);
		PluginGalleryEntry entry = await FindAsync(gallery, "acme.proxmox");

		using IPluginPackage read = await gallery.DownloadAsync(entry, entry.Newest!, Ct).WaitAsync(Limit, Ct);

		Assert.Equal("acme.proxmox", read.Id);
		Assert.Equal("1.2.0", read.Version);
		Assert.True(read.HasCode);
		Assert.Null(read.Problem);
		Assert.Equal(new PluginPackageOrigin(new Uri("https://github.com/acme/mokaterm-tests"), TestGallery.Commit, Verified: true), read.Origin);
		Assert.Equal(TestGallery.PackageAddress("acme.proxmox", "1.2.0"), server.Server.Requests[^1].RequestUri);

		// What the gallery hands over is what a picked file gives: the manager installs it.
		await _manager.InstallAsync(read, enable: false, Ct).WaitAsync(Limit, Ct);
		Assert.Equal(PluginPendingChange.Install, Assert.Single(_manager.Plugins).Pending);
	}

	[Fact]
	public async Task AWindowDownload_IsKeptUntilTaken_SoAPageLeftAndReturnedCanStillOpenIt()
	{
		TestGallery server = new();
		byte[] package = TestPlugins.Zip(TestPlugins.CodePlugin("acme.proxmox", "1.2.0"));
		server.Publish(TestGallery.Index(TestGallery.Plugin("acme.proxmox", hasCode: true, TestGallery.Version("acme.proxmox", "1.2.0", package))));
		server.Host("acme.proxmox", "1.2.0", package);
		using PluginGallery gallery = server.Gallery(_manager);
		PluginGalleryEntry entry = await FindAsync(gallery, "acme.proxmox");
		int changes = 0;
		gallery.Changed += (_, _) => changes++;

		await gallery.BeginDownloadAsync(entry, entry.Newest!).WaitAsync(Limit, Ct);

		PluginGalleryDownload? download = gallery.Download;
		Assert.NotNull(download);
		Assert.True(download.Done);
		Assert.Null(download.Problem);
		Assert.Equal("acme.proxmox", download.Package!.Id);
		Assert.Equal("1.2.0", download.Version);

		// Taken once: nothing waits after that, and a second take hands back nothing.
		using IPluginPackage? taken = gallery.TakeDownload();
		Assert.Same(download.Package, taken);
		Assert.Null(gallery.Download);
		Assert.Null(gallery.TakeDownload());
		Assert.True(changes >= 2);
	}

	[Fact]
	public async Task AWindowDownload_ThatFails_RecordsWhyWithoutThrowing()
	{
		TestGallery server = new();
		byte[] package = TestPlugins.Zip(TestPlugins.ThemePack("acme.themes"));
		server.Publish(TestGallery.Index(TestGallery.Plugin("acme.themes", hasCode: false, TestGallery.Version("acme.themes", "1.0.0", package))));
		server.Server.Refuse(TestGallery.PackageAddress("acme.themes", "1.0.0"), HttpStatusCode.NotFound);
		using PluginGallery gallery = server.Gallery(_manager);
		PluginGalleryEntry entry = await FindAsync(gallery, "acme.themes");

		await gallery.BeginDownloadAsync(entry, entry.Newest!).WaitAsync(Limit, Ct);

		PluginGalleryDownload? download = gallery.Download;
		Assert.NotNull(download);
		Assert.True(download.Done);
		Assert.Null(download.Package);
		Assert.Contains("answered 404", download.Problem, StringComparison.Ordinal);
		Assert.Null(gallery.TakeDownload());

		// A failed download does not block the next one, and its record gives way to it.
		server.Host("acme.themes", "1.0.0", package);
		await gallery.BeginDownloadAsync(entry, entry.Newest!).WaitAsync(Limit, Ct);
		Assert.NotNull(gallery.Download!.Package);
	}

	[Fact]
	public async Task ASecondWindowDownload_ReplacesThePackageNobodyTook()
	{
		TestGallery server = new();
		byte[] package = TestPlugins.Zip(TestPlugins.ThemePack("acme.themes"));
		server.Publish(TestGallery.Index(TestGallery.Plugin("acme.themes", hasCode: false, TestGallery.Version("acme.themes", "1.0.0", package))));
		server.Host("acme.themes", "1.0.0", package);
		using PluginGallery gallery = server.Gallery(_manager);
		PluginGalleryEntry entry = await FindAsync(gallery, "acme.themes");

		await gallery.BeginDownloadAsync(entry, entry.Newest!).WaitAsync(Limit, Ct);
		await gallery.BeginDownloadAsync(entry, entry.Newest!).WaitAsync(Limit, Ct);

		// What waits is the second download's package, not a disposed leftover.
		PluginGalleryDownload? download = gallery.Download;
		Assert.NotNull(download);
		Assert.True(download.Done);
		Assert.NotNull(gallery.TakeDownload());
	}

	[Fact]
	public async Task Download_OfAnUpdate_SaysWhichVersionIsInstalled()
	{
		using (IPluginPackage installed = await _manager.ReadPackageAsync(new MemoryStream(TestPlugins.Zip(TestPlugins.ThemePack("acme.themes", "1.0.0"))), origin: null, Ct).WaitAsync(Limit, Ct))
		{
			await _manager.InstallAsync(installed, enable: true, Ct).WaitAsync(Limit, Ct);
		}

		TestGallery server = new();
		byte[] package = TestPlugins.Zip(TestPlugins.ThemePack("acme.themes", "1.1.0"));
		server.Publish(TestGallery.Index(TestGallery.Plugin("acme.themes", hasCode: false, TestGallery.Version("acme.themes", "1.1.0", package))));
		server.Host("acme.themes", "1.1.0", package);
		using PluginGallery gallery = server.Gallery(_manager);
		PluginGalleryEntry entry = await FindAsync(gallery, "acme.themes");

		using IPluginPackage read = await gallery.DownloadAsync(entry, entry.Newest!, Ct).WaitAsync(Limit, Ct);

		Assert.Equal("1.0.0", read.InstalledVersion);
		Assert.False(read.Origin!.Verified);
	}

	[Fact]
	public async Task Download_ShorterThanListed_IsRefused()
	{
		byte[] package = TestPlugins.Zip(TestPlugins.ThemePack("acme.themes"));
		PluginGalleryException error = await DownloadServedAsync(package, package[..^10]);

		Assert.Contains($"the gallery lists {package.Length}", error.Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Download_LongerThanListed_IsStopped(bool hideLength)
	{
		byte[] package = TestPlugins.Zip(TestPlugins.ThemePack("acme.themes"));
		PluginGalleryException error = await DownloadServedAsync(package, [.. package, .. new byte[4096]], hideLength);

		Assert.Equal("The download is larger than the gallery says it is, so it was stopped.", error.Message);
	}

	[Fact]
	public async Task Download_WithAnotherHash_IsRefused()
	{
		byte[] package = TestPlugins.Zip(TestPlugins.ThemePack("acme.themes"));
		byte[] changed = (byte[])package.Clone();
		changed[^1] ^= 0xFF;

		PluginGalleryException error = await DownloadServedAsync(package, changed);

		Assert.Contains("does not match the SHA-256 the gallery lists", error.Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("acme.other", "1.0.0")]
	[InlineData("acme.themes", "1.0.1")]
	public async Task Download_OfAnotherPluginOrVersion_IsRefused(string packageId, string packageVersion)
	{
		// The list's hash matches the package, but the package says it is something other than what was chosen.
		TestGallery server = new();
		byte[] package = TestPlugins.Zip(TestPlugins.ThemePack(packageId, packageVersion));
		server.Publish(TestGallery.Index(TestGallery.Plugin("acme.themes", hasCode: false, TestGallery.Version("acme.themes", "1.0.0", package))));
		server.Host("acme.themes", "1.0.0", package);
		using PluginGallery gallery = server.Gallery(_manager);
		PluginGalleryEntry entry = await FindAsync(gallery, "acme.themes");

		PluginGalleryException error = await Assert.ThrowsAsync<PluginGalleryException>(
			() => gallery.DownloadAsync(entry, entry.Newest!, Ct).WaitAsync(Limit, Ct));

		Assert.Contains($"The download is {packageId} {packageVersion}, not the acme.themes 1.0.0 the gallery lists", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Download_ThatCarriesCodeWhereTheListSaysData_IsRefused()
	{
		TestGallery server = new();
		byte[] package = TestPlugins.Zip(TestPlugins.CodePlugin("acme.themes"));
		server.Publish(TestGallery.Index(TestGallery.Plugin("acme.themes", hasCode: false, TestGallery.Version("acme.themes", "1.0.0", package))));
		server.Host("acme.themes", "1.0.0", package);
		using PluginGallery gallery = server.Gallery(_manager);
		PluginGalleryEntry entry = await FindAsync(gallery, "acme.themes");

		PluginGalleryException error = await Assert.ThrowsAsync<PluginGalleryException>(
			() => gallery.DownloadAsync(entry, entry.Newest!, Ct).WaitAsync(Limit, Ct));

		Assert.Contains("as data only, but the download carries code", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Download_ThatIsNotAPackage_SaysWhatTheManagerSaid()
	{
		TestGallery server = new();
		byte[] notAZip = "this is not a zip"u8.ToArray();
		server.Publish(TestGallery.Index(TestGallery.Plugin("acme.themes", hasCode: false, TestGallery.Version("acme.themes", "1.0.0", notAZip))));
		server.Host("acme.themes", "1.0.0", notAZip);
		using PluginGallery gallery = server.Gallery(_manager);
		PluginGalleryEntry entry = await FindAsync(gallery, "acme.themes");

		PluginGalleryException error = await Assert.ThrowsAsync<PluginGalleryException>(
			() => gallery.DownloadAsync(entry, entry.Newest!, Ct).WaitAsync(Limit, Ct));

		Assert.Contains("not a readable zip", error.Message, StringComparison.Ordinal);
		Assert.IsType<InvalidDataException>(error.InnerException);
	}

	[Fact]
	public async Task Download_OfAVersionNotOffered_IsNeverFetched()
	{
		TestGallery server = new();
		byte[] package = TestPlugins.Zip(TestPlugins.ThemePack("acme.themes"));
		server.Publish(TestGallery.Index(TestGallery.Plugin(
			"acme.themes",
			hasCode: false,
			TestGallery.Version("acme.themes", "1.0.0", package, withdrawn: "Its colours were unreadable."))));
		server.Host("acme.themes", "1.0.0", package);
		using PluginGallery gallery = server.Gallery(_manager);
		PluginGalleryEntry entry = await FindAsync(gallery, "acme.themes");
		int asked = server.Server.Requests.Count;

		PluginGalleryException error = await Assert.ThrowsAsync<PluginGalleryException>(
			() => gallery.DownloadAsync(entry, entry.Versions[0], Ct).WaitAsync(Limit, Ct));

		Assert.Contains("Every version of it was withdrawn.", error.Message, StringComparison.Ordinal);
		Assert.Equal(asked, server.Server.Requests.Count);
	}

	[Fact]
	public async Task OnTheWebHost_APluginForTheDesktop_IsNotOffered()
	{
		TestGallery server = new();
		JsonObject plugin = TestGallery.Plugin("acme.serial", hasCode: true, TestGallery.Version("1.0.0"));
		plugin["hosts"] = new JsonArray("desktop");
		server.Publish(TestGallery.Index(plugin));
		using PluginGallery gallery = server.Gallery(_manager, HostKind.Web);

		PluginGalleryEntry entry = await FindAsync(gallery, "acme.serial");

		Assert.Null(entry.Newest);
		Assert.Equal("This plugin is for the desktop app only.", entry.Unavailable);
	}

	[Fact]
	public void AddPluginGallery_GivesEachScopeItsOwnList_OverOneClient()
	{
		ServiceCollection services = new();
		services.AddSingleton<IAppEnvironment>(new FakeAppEnvironment(Path.GetTempPath()));
		services.AddSingleton<IPluginManager>(_manager);
		services.AddPluginGallery(new PluginGalleryOptions(), () => new FakeGalleryServer());
		using ServiceProvider provider = services.BuildServiceProvider();
		using IServiceScope first = provider.CreateScope();
		using IServiceScope second = provider.CreateScope();

		IPluginGallery one = first.ServiceProvider.GetRequiredService<IPluginGallery>();
		IPluginGallery other = second.ServiceProvider.GetRequiredService<IPluginGallery>();

		Assert.NotSame(one, other);
		Assert.Same(one, first.ServiceProvider.GetRequiredService<IPluginGallery>());
		Assert.Same(provider.GetRequiredService<PluginGalleryClient>(), provider.GetRequiredService<PluginGalleryClient>());
		Assert.Equal(PluginGalleryOptions.ProductionIndex, one.Index);
		Assert.Null(one.Current);
	}

	private static async Task<PluginGalleryEntry> FindAsync(PluginGallery gallery, string id) =>
		(await gallery.ReadIndexAsync(refresh: false, Ct).WaitAsync(Limit, Ct)).Find(id) ?? throw new InvalidOperationException($"{id} is not listed.");

	/// <summary>Lists <paramref name="listed"/> and serves <paramref name="served"/> in its place, then downloads it.</summary>
	private async Task<PluginGalleryException> DownloadServedAsync(byte[] listed, byte[] served, bool hideLength = false)
	{
		TestGallery server = new();
		server.Publish(TestGallery.Index(TestGallery.Plugin("acme.themes", hasCode: false, TestGallery.Version("acme.themes", "1.0.0", listed))));
		server.Server.Serve(TestGallery.PackageAddress("acme.themes", "1.0.0"), served, hideLength);
		using PluginGallery gallery = server.Gallery(_manager);
		PluginGalleryEntry entry = await FindAsync(gallery, "acme.themes");

		return await Assert.ThrowsAsync<PluginGalleryException>(() => gallery.DownloadAsync(entry, entry.Newest!, Ct).WaitAsync(Limit, Ct));
	}
}
