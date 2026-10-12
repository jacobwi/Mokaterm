using Mokaterm.Abstractions.Protocols;
using Mokaterm.UI.Common.Files;
using Mokaterm.UI.FileBrowser.Browsing;
using Mokaterm.UI.FileBrowser.Tests.Fakes;
using Mokaterm.UI.FileBrowser.Transfers;

namespace Mokaterm.UI.FileBrowser.Tests;

/// <summary>
/// The two things a browser can show, a session tab and a file system a module handed over, answer the browser's
/// questions in one place, so everything past them (opening, listing, transfers) is one path.
/// </summary>
public sealed class BrowserSourceTests
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public void AModulesFiles_AreTheirOwnConnection_WithNoneOfASessionsParts()
	{
		FakeFileFeature feature = new(new FakeFileSystem()) { SupportsElevation = false };
		FileBrowserRequest request = Request(feature);

		BrowserSource source = BrowserSource.For(request);

		Assert.Same(request, source.Key);
		Assert.Same(feature, source.Connection);
		Assert.Same(feature, source.FeatureOf(source.Connection!));
		Assert.Equal("web-1", source.Location);
		Assert.Equal("web-1 on build-01", source.Group);
		Assert.Null(source.Session);
		Assert.Null(source.Terminal);
		Assert.Null(source.BookmarkOwner);
		Assert.Null(source.ConfiguredStartFolder(["ssh.initialDirectory"]));
	}

	[Fact]
	public void ASessionsFiles_FollowItsConnectionAcrossAReconnect()
	{
		FakeFileFeature feature = new(new FakeFileSystem());
		FakeTab tab = new(feature);
		BrowserSource source = BrowserSource.For(tab);

		IProtocolSession first = Assert.IsAssignableFrom<IProtocolSession>(source.Connection);
		Assert.Same(feature, source.FeatureOf(first));
		Assert.Same(tab, source.Key);
		Assert.Equal("build-01", source.Location);
		Assert.Equal(tab.Title, source.Group);
		Assert.Equal(tab.Connection.Id, source.BookmarkOwner);

		tab.Reconnect();
		Assert.NotSame(first, source.Connection);

		tab.Disconnect();
		Assert.Null(source.Connection);
	}

	[Fact]
	public void AQuickConnection_HasNoLoginToKeepBookmarksUnder()
	{
		BrowserSource source = BrowserSource.For(new FakeTab(new FakeFileFeature(new FakeFileSystem())) { IsTransient = true });

		Assert.Null(source.BookmarkOwner);
	}

	// A retry after the module's file system was reopened must not reach for the one the transfer was queued with.
	[Fact]
	public async Task TransfersFromAModulesFiles_AskTheFeatureEveryTimeTheyRun()
	{
		FakeFileSystem queuedWith = new();
		FakeFileFeature feature = new(queuedWith) { SupportsElevation = false };
		BrowserSource source = BrowserSource.For(Request(feature));
		TransferFileSystem transfers = source.ForTransfers(queuedWith, source.Connection);

		Assert.Same(queuedWith, await transfers.GetAsync(Ct));
		FakeFileSystem reopened = new();
		feature.User = reopened;

		Assert.Same(reopened, await transfers.GetAsync(Ct));
		Assert.Equal(2, feature.Opens);
		Assert.False(transfers.IsElevated);
	}

	private static FileBrowserRequest Request(FakeFileFeature feature) => new()
	{
		FileSystem = feature,
		Title = "web-1 on build-01",
		Location = "web-1",
		StartFolder = "/usr/share/nginx/html",
	};
}
