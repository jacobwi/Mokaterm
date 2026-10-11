using Mokaterm.Abstractions.Security;
using Mokaterm.UI.FileBrowser.Bookmarks;
using Mokaterm.UI.FileBrowser.Tests.Fakes;

namespace Mokaterm.UI.FileBrowser.Tests;

public sealed class FolderBookmarkStoreTests
{
	private static readonly Guid Login = Guid.NewGuid();
	private static readonly Guid Other = Guid.NewGuid();

	[Fact]
	public async Task Add_KeepsTheOrderAndNormalizesWithoutDuplicates()
	{
		using FolderBookmarkStore store = new(new MemoryVaultDataStore(), new FixedConnections(Login));

		await store.AddAsync(Login, "/etc/nginx/", TestContext.Current.CancellationToken);
		await store.AddAsync(Login, "/var//log", TestContext.Current.CancellationToken);
		IReadOnlyList<string> bookmarks = await store.AddAsync(Login, "/etc/nginx", TestContext.Current.CancellationToken);

		Assert.Equal(["/etc/nginx", "/var/log"], bookmarks);
		Assert.Equal(bookmarks, await store.ListAsync(Login, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task Bookmarks_BelongToOneLogin()
	{
		using FolderBookmarkStore store = new(new MemoryVaultDataStore(), new FixedConnections(Login, Other));

		await store.AddAsync(Login, "/srv/app", TestContext.Current.CancellationToken);
		await store.AddAsync(Other, "/opt", TestContext.Current.CancellationToken);

		Assert.Equal(["/srv/app"], await store.ListAsync(Login, TestContext.Current.CancellationToken));
		Assert.Equal(["/opt"], await store.ListAsync(Other, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task Remove_TakesOutOnePathAndTheLastOneDeletesTheDocument()
	{
		MemoryVaultDataStore vault = new();
		using FolderBookmarkStore store = new(vault, new FixedConnections(Login));
		await store.AddAsync(Login, "/a", TestContext.Current.CancellationToken);
		await store.AddAsync(Login, "/b", TestContext.Current.CancellationToken);

		Assert.Equal(["/b"], await store.RemoveAsync(Login, "/a/", TestContext.Current.CancellationToken));
		Assert.Empty(await store.RemoveAsync(Login, "/b", TestContext.Current.CancellationToken));
		Assert.Empty(vault.Documents);
	}

	// Paths describe the server, so they go to the vault and only there, in the document of their own.
	[Fact]
	public async Task Bookmarks_AreStoredInTheVaultDocument()
	{
		MemoryVaultDataStore vault = new();
		using FolderBookmarkStore store = new(vault, new FixedConnections(Login));

		await store.AddAsync(Login, "/srv/secret-project", TestContext.Current.CancellationToken);

		string document = Assert.Single(vault.Documents).Value;
		Assert.Equal(FolderBookmarksDocument.DocumentName, Assert.Single(vault.Documents.Keys));
		Assert.Contains("/srv/secret-project", document, StringComparison.Ordinal);
		Assert.Contains(Login.ToString(), document, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task A_DeletedLogin_LosesItsBookmarksWithTheNextChange()
	{
		MemoryVaultDataStore vault = new();
		FixedConnections connections = new(Login, Other);
		using FolderBookmarkStore store = new(vault, connections);
		await store.AddAsync(Other, "/gone", TestContext.Current.CancellationToken);

		connections.Catalog = new FixedConnections(Login).Catalog;
		await store.AddAsync(Login, "/kept", TestContext.Current.CancellationToken);

		Assert.Empty(await store.ListAsync(Other, TestContext.Current.CancellationToken));
		Assert.DoesNotContain("/gone", Assert.Single(vault.Documents).Value, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Add_PastTheCap_IsRefused()
	{
		using FolderBookmarkStore store = new(new MemoryVaultDataStore(), new FixedConnections(Login));
		for (int index = 0; index < FolderBookmarkStore.MaxPerLogin; index++)
		{
			await store.AddAsync(Login, "/folder" + index, TestContext.Current.CancellationToken);
		}

		await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddAsync(Login, "/one-more", TestContext.Current.CancellationToken));
		Assert.Equal(FolderBookmarkStore.MaxPerLogin, (await store.ListAsync(Login, TestContext.Current.CancellationToken)).Count);
	}

	[Fact]
	public async Task A_LockedVault_SaysSo()
	{
		using FolderBookmarkStore store = new(new MemoryVaultDataStore { Locked = true }, new FixedConnections(Login));

		await Assert.ThrowsAsync<VaultLockedException>(async () => await store.ListAsync(Login, TestContext.Current.CancellationToken));
		await Assert.ThrowsAsync<VaultLockedException>(() => store.AddAsync(Login, "/a", TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task Changed_IsRaisedForTheBookmarksDocumentOnly()
	{
		MemoryVaultDataStore vault = new();
		using FolderBookmarkStore store = new(vault, new FixedConnections(Login));
		int changes = 0;
		store.Changed += () => changes++;

		await vault.WriteAsync("other-module", new FolderBookmarksDocument(), TestContext.Current.CancellationToken);
		await store.AddAsync(Login, "/a", TestContext.Current.CancellationToken);

		Assert.Equal(1, changes);
	}
}
