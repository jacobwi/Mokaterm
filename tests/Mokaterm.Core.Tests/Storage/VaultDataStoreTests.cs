using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Storage;
using Mokaterm.Core.Storage;
using Mokaterm.Core.Tests.TestSupport;

namespace Mokaterm.Core.Tests.Storage;

public sealed class VaultDataStoreTests
{
	private const string Marker = "mokaterm-plaintext-marker-5d41402abc4b2a76";

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task WriteAsync_ThenRead_RoundTrips()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		IVaultDataStore store = Store(scope);
		Bookmark bookmark = new(Marker, ["prod", "db"], 42);

		await store.WriteAsync("bookmarks", bookmark, Ct);
		Bookmark? read = await store.ReadAsync<Bookmark>("bookmarks", Ct);

		Assert.NotNull(read);
		Assert.Equal(bookmark.Title, read.Title);
		Assert.Equal(bookmark.Tags, read.Tags);
		Assert.Equal(bookmark.Visits, read.Visits);
		Assert.Null(await store.ReadAsync<Bookmark>("missing", Ct));
	}

	[Fact]
	public async Task WriteAsync_StoresEnvelopeWithoutPlaintext()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();

		await Store(scope).WriteAsync("bookmarks", new Bookmark(Marker, [Marker], 1), Ct);

		byte[] raw = await File.ReadAllBytesAsync(Path.Combine(context.VaultDirectory, "bookmarks.vault"), Ct);
		Assert.Equal("MKV1"u8.ToArray(), raw[..4]);
		Assert.True(raw.Length > VaultEnvelope.MagicSize + 28);
		Assert.Equal(-1, raw.AsSpan().IndexOf(Encoding.UTF8.GetBytes(Marker)));
		Assert.Equal(-1, raw.AsSpan().IndexOf(Encoding.UTF8.GetBytes("title")));
	}

	[Fact]
	public async Task Members_WhileLocked_Throw()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		IVaultDataStore store = Store(scope);
		await store.WriteAsync("bookmarks", new Bookmark("x", [], 0), Ct);
		scope.ServiceProvider.GetRequiredService<IVault>().Lock();

		await Assert.ThrowsAsync<VaultLockedException>(async () => await store.ReadAsync<Bookmark>("bookmarks", Ct));
		await Assert.ThrowsAsync<VaultLockedException>(() => store.WriteAsync("bookmarks", new Bookmark("y", [], 0), Ct));
		await Assert.ThrowsAsync<VaultLockedException>(() => store.DeleteAsync("bookmarks", Ct));
		Assert.True(File.Exists(Path.Combine(context.VaultDirectory, "bookmarks.vault")));
	}

	[Theory]
	[InlineData(0)]
	[InlineData(VaultEnvelope.MagicSize + 3)]
	[InlineData(-1)]
	public async Task ReadAsync_TamperedFile_IsQuarantinedAndReadAsNull(int index)
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		IVaultDataStore store = Store(scope);
		await store.WriteAsync("bookmarks", new Bookmark("x", [], 0), Ct);
		string path = Path.Combine(context.VaultDirectory, "bookmarks.vault");
		byte[] raw = await File.ReadAllBytesAsync(path, Ct);
		raw[index < 0 ? raw.Length - 1 : index] ^= 0x20;
		await File.WriteAllBytesAsync(path, raw, Ct);

		Assert.Null(await store.ReadAsync<Bookmark>("bookmarks", Ct));

		Assert.False(File.Exists(path));
		Assert.Single(Directory.GetFiles(context.VaultDirectory, "bookmarks.corrupt-*.vault"));
	}

	// A damaged document used to disappear without a word: the list it held just showed empty, and the next change wrote
	// an empty document over it.
	[Fact]
	public async Task ReadAsync_DamagedFile_TellsEveryScopeWhatWasSetAsideAndWhere()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope first = await context.CreateVaultScopeAsync();
		await using AsyncServiceScope second = await context.UnlockedScopeAsync();
		IVaultDataStore reader = Store(first);
		List<DamagedDocument> firstHeard = [];
		List<DamagedDocument> secondHeard = [];
		reader.DocumentDamaged += firstHeard.Add;
		Store(second).DocumentDamaged += secondHeard.Add;
		await reader.WriteAsync("bookmarks", new Bookmark("x", [], 0), Ct);
		string path = Path.Combine(context.VaultDirectory, "bookmarks.vault");
		byte[] raw = await File.ReadAllBytesAsync(path, Ct);
		raw[^1] ^= 0x20;
		await File.WriteAllBytesAsync(path, raw, Ct);

		Assert.Null(await reader.ReadAsync<Bookmark>("bookmarks", Ct));

		DamagedDocument damaged = Assert.Single(firstHeard);
		Assert.Equal("bookmarks", damaged.Name);
		Assert.True(damaged.MovedAside);
		Assert.Equal(Assert.Single(Directory.GetFiles(context.VaultDirectory, "bookmarks.corrupt-*.vault")), damaged.Path);
		Assert.Equal(damaged, Assert.Single(secondHeard));
	}

	[Fact]
	public async Task DisposedScope_StopsHearingAboutDamagedDocuments()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope first = await context.CreateVaultScopeAsync();
		int heard = 0;
		await using (AsyncServiceScope second = await context.UnlockedScopeAsync())
		{
			Store(second).DocumentDamaged += _ => heard++;
		}

		Directory.CreateDirectory(context.VaultDirectory);
		await File.WriteAllBytesAsync(Path.Combine(context.VaultDirectory, "bookmarks.vault"), "MKV1 not a vault document"u8.ToArray(), Ct);
		Assert.Null(await Store(first).ReadAsync<Bookmark>("bookmarks", Ct));

		Assert.Equal(0, heard);
	}

	[Fact]
	public async Task ReadAsync_DocumentCopiedUnderAnotherName_DoesNotDecrypt()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		IVaultDataStore store = Store(scope);
		await store.WriteAsync("bookmarks", new Bookmark("x", [], 0), Ct);
		File.Copy(Path.Combine(context.VaultDirectory, "bookmarks.vault"), Path.Combine(context.VaultDirectory, "renamed.vault"));

		Assert.Null(await store.ReadAsync<Bookmark>("renamed", Ct));
		Assert.NotNull(await store.ReadAsync<Bookmark>("bookmarks", Ct));
	}

	[Fact]
	public async Task WriteAndDelete_RaiseDocumentChangedInEveryScope()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope first = await context.CreateVaultScopeAsync();
		await using AsyncServiceScope second = await context.UnlockedScopeAsync();
		VaultDataStore firstStore = first.ServiceProvider.GetRequiredService<VaultDataStore>();
		VaultDataStore secondStore = second.ServiceProvider.GetRequiredService<VaultDataStore>();
		List<string> firstChanges = [];
		List<string> firstExternal = [];
		List<string> secondChanges = [];
		List<string> secondExternal = [];
		firstStore.DocumentChanged += firstChanges.Add;
		firstStore.ExternalDocumentChanged += firstExternal.Add;
		secondStore.DocumentChanged += secondChanges.Add;
		secondStore.ExternalDocumentChanged += secondExternal.Add;

		await firstStore.WriteAsync("bookmarks", new Bookmark("x", [], 0), Ct);
		await firstStore.DeleteAsync("bookmarks", Ct);
		await firstStore.DeleteAsync("bookmarks", Ct);

		Assert.Equal(2, firstChanges.Count);
		Assert.Empty(firstExternal);
		Assert.Equal(2, secondChanges.Count);
		Assert.Equal(2, secondExternal.Count);
		Assert.All(secondExternal, name => Assert.Equal("bookmarks", name));
		Assert.Null(await secondStore.ReadAsync<Bookmark>("bookmarks", Ct));
	}

	[Fact]
	public async Task DisposedScope_StopsReceivingChanges()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope first = await context.CreateVaultScopeAsync();
		int secondChanges = 0;
		await using (AsyncServiceScope second = await context.UnlockedScopeAsync())
		{
			Store(second).DocumentChanged += _ => secondChanges++;
			await Store(first).WriteAsync("bookmarks", new Bookmark("x", [], 0), Ct);
		}

		await Store(first).WriteAsync("bookmarks", new Bookmark("y", [], 0), Ct);

		Assert.Equal(1, secondChanges);
	}

	[Fact]
	public async Task Scope_ResolvesOneStoreForInterfaceAndImplementation()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = context.CreateScope();

		Assert.Same(scope.ServiceProvider.GetRequiredService<VaultDataStore>(), Store(scope));
	}

	private static IVaultDataStore Store(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<IVaultDataStore>();

	public sealed record Bookmark(string Title, IReadOnlyList<string> Tags, int Visits);
}
