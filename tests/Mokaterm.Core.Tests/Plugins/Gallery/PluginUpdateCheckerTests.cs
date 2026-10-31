using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Plugins;
using Mokaterm.Core.Plugins;
using Mokaterm.Core.Plugins.Gallery;
using Mokaterm.Tests.Shared;

namespace Mokaterm.Core.Tests.Plugins.Gallery;

/// <summary>
/// The startup check's half of the gallery: which installed plugins have a newer version on the signed list, offered
/// the way the Browse tab's cards would offer it, and a check that fails reads nothing and says nothing.
/// </summary>
public sealed class PluginUpdateCheckerTests : IDisposable
{
	private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

	private readonly TempPluginsFolder _folder = new();

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	public void Dispose() => _folder.Dispose();

	private static PluginRecord Installed(string id, string version, PluginPendingChange pending = PluginPendingChange.None) => new()
	{
		Id = id,
		Name = id,
		Version = version,
		HasCode = false,
		Status = PluginStatus.Enabled,
		Pending = pending,
	};

	[Fact]
	public void For_AnOlderInstalledVersion_OffersTheNewest()
	{
		// The list names its versions newest first, the format's rule; the reader keeps that order.
		PluginGalleryIndex index = Read(Entry("acme.themes", ("1.2.0", null), ("1.0.0", null)));

		IReadOnlyList<PluginUpdateOffer> offers = PluginUpdateChecker.For(index, [Installed("acme.themes", "1.0.0")]);

		PluginUpdateOffer offer = Assert.Single(offers);
		Assert.Equal("acme.themes", offer.PluginId);
		Assert.Equal("1.0.0", offer.InstalledVersion);
		Assert.Equal("1.2.0", offer.NewVersion);
	}

	[Fact]
	public void For_TheNewestInstalled_OffersNothing()
	{
		PluginGalleryIndex index = Read(Entry("acme.themes", ("1.2.0", null)));

		Assert.Empty(PluginUpdateChecker.For(index, [Installed("acme.themes", "1.2.0")]));
	}

	[Fact]
	public void For_APluginTheListDoesNotName_OffersNothing()
	{
		// Installed from a file, or the list dropped it: there is nothing to compare, so there is no update.
		PluginGalleryIndex index = Read(Entry("acme.themes", ("1.2.0", null)));

		Assert.Empty(PluginUpdateChecker.For(index, [Installed("acme.other", "1.0.0")]));
	}

	[Fact]
	public void For_APluginOnItsWayOut_OffersNothing()
	{
		PluginGalleryIndex index = Read(Entry("acme.themes", ("1.2.0", null)));

		Assert.Empty(PluginUpdateChecker.For(index, [Installed("acme.themes", "1.0.0", PluginPendingChange.Remove)]));
	}

	[Fact]
	public void For_AWithdrawnNewest_OffersTheNextOne()
	{
		// Withdrawn versions are never offered, so the update is 1.1.0, not the withdrawn 1.2.0.
		PluginGalleryIndex index = Read(Entry("acme.themes", ("1.2.0", "Its selection colour hid the cursor."), ("1.1.0", null), ("1.0.0", null)));

		IReadOnlyList<PluginUpdateOffer> offers = PluginUpdateChecker.For(index, [Installed("acme.themes", "1.0.0")]);

		Assert.Equal("1.1.0", Assert.Single(offers).NewVersion);
	}

	[Fact]
	public async Task CheckAsync_ReadsTheList_AndKeepsItForTheWindow()
	{
		TestGallery server = new();
		byte[] installed = TestPlugins.Zip(TestPlugins.ThemePack("acme.themes", "1.0.0"));
		server.Publish(TestGallery.Index(TestGallery.Plugin("acme.themes", hasCode: false, Version("acme.themes", "1.1.0"))));
		PluginIndexCache cache = new();
		using PluginManager manager = await ManagerWithInstalledAsync(installed);
		PluginUpdateChecker checker = MakeChecker(server, cache, manager);

		IReadOnlyList<PluginUpdateOffer>? offers = await checker.CheckAsync(Ct).WaitAsync(Limit, Ct);

		Assert.NotNull(offers);
		PluginUpdateOffer offer = Assert.Single(offers);
		Assert.Equal("1.1.0", offer.NewVersion);

		// A window that has read nothing sees the list the check trusted, so its rows show chips without asking.
		using PluginManager windowManager = new(_folder.Open());
		using PluginGallery window = new(server.Client(), windowManager, new FakeAppEnvironment(Path.GetTempPath()), cache);
		Assert.NotNull(window.Current);
		Assert.NotNull(window.Current!.Find("acme.themes"));
	}

	[Fact]
	public async Task CheckAsync_AListThatCannotBeRead_SaysNothing()
	{
		TestGallery server = new();
		server.Publish(TestGallery.Index(TestGallery.Plugin("acme.themes", hasCode: false, Version("acme.themes", "1.1.0"))));
		server.Server.Refuse(TestGallery.IndexAddress, HttpStatusCode.NotFound);
		byte[] installed = TestPlugins.Zip(TestPlugins.ThemePack("acme.themes", "1.0.0"));
		using PluginManager manager = await ManagerWithInstalledAsync(installed);
		PluginUpdateChecker checker = MakeChecker(server, new PluginIndexCache(), manager);

		Assert.Null(await checker.CheckAsync(Ct).WaitAsync(Limit, Ct));
		Assert.Null(checker.Latest);
	}

	/// <summary>Installs a real 1.0.0 package, so the manager holds the same record shape a user's install does.</summary>
	private async Task<PluginManager> ManagerWithInstalledAsync(byte[] package)
	{
		PluginManager manager = new(_folder.Open());
		using MemoryStream stream = new(package, writable: false);
		using IPluginPackage read = await manager.ReadPackageAsync(stream, origin: null, Ct).WaitAsync(Limit, Ct);
		await manager.InstallAsync(read, enable: false, Ct).WaitAsync(Limit, Ct);
		return manager;
	}

	private static PluginUpdateChecker MakeChecker(TestGallery server, PluginIndexCache cache, PluginManager manager) => new(
		server.Client(),
		cache,
		manager,
		new FakeAppEnvironment(Path.GetTempPath()),
		NullLogger<PluginUpdateChecker>.Instance);

	private static JsonObject Version(string id, string version) =>
		TestGallery.Version(id, version, Encoding.ASCII.GetBytes(id + version));

	private static JsonObject Entry(string id, params (string Version, string? Withdrawn)[] versions) =>
		TestGallery.Plugin(id, hasCode: false, [.. versions.Select(v => TestGallery.Version(id, v.Version, Encoding.ASCII.GetBytes(id + v.Version), withdrawn: v.Withdrawn))]);

	private static PluginGalleryIndex Read(JsonObject plugin)
	{
		TestGallery server = new();
		byte[] signed = server.Publish(TestGallery.Index(plugin));
		return PluginIndexReader.Read(signed, PluginHosts.Desktop);
	}
}
