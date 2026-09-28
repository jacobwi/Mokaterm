using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Storage;
using Mokaterm.Core.Tests.TestSupport;

namespace Mokaterm.Core.Tests.Storage;

public sealed class FileAppDataStoreTests
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task ReadAsync_MissingDocument_ReturnsNull()
	{
		await using CoreTestContext context = new();

		Assert.Null(await Store(context).ReadAsync<Layout>("layout", Ct));
	}

	[Fact]
	public async Task WriteAsync_ThenRead_RoundTripsAsCamelCaseJsonWithEnumNames()
	{
		await using CoreTestContext context = new();
		IAppDataStore store = Store(context);
		Layout layout = new("sessions", ThemeMode.Light, 320);

		await store.WriteAsync("layout", layout, Ct);

		Assert.Equal(layout, await store.ReadAsync<Layout>("layout", Ct));
		string json = await File.ReadAllTextAsync(Path.Combine(context.DataDirectory, "layout.json"), Ct);
		Assert.Contains("\"sidebarWidth\": 320", json, StringComparison.Ordinal);
		Assert.Contains("\"mode\": \"Light\"", json, StringComparison.Ordinal);
	}

	[Fact]
	public async Task WriteAsync_ReplacesDocumentAndLeavesNoTemporaryFiles()
	{
		await using CoreTestContext context = new();
		IAppDataStore store = Store(context);

		await store.WriteAsync("layout", new Layout("first", ThemeMode.Dark, 100), Ct);
		await store.WriteAsync("layout", new Layout("second", ThemeMode.Dark, 200), Ct);

		Assert.Equal("second", (await store.ReadAsync<Layout>("layout", Ct))?.ActivePanel);
		Assert.Equal("layout.json", Path.GetFileName(Assert.Single(Directory.GetFiles(context.DataDirectory))));
	}

	[Fact]
	public async Task WriteAsync_OnUnix_CreatesFilesAndFoldersForTheirOwnerOnly()
	{
		if (OperatingSystem.IsWindows())
		{
			Assert.Skip("Windows files take their directory's ACL; there is no mode to check.");
			return;
		}

		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		await scope.ServiceProvider.GetRequiredService<IVaultDataStore>().WriteAsync("notes", new Layout("x", ThemeMode.Dark, 1), Ct);
		await Store(context).WriteAsync("layout", new Layout("y", ThemeMode.Dark, 2), Ct);

		UnixFileMode ownerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;
		Assert.Equal(ownerOnly, File.GetUnixFileMode(context.HeaderPath));
		Assert.Equal(ownerOnly, File.GetUnixFileMode(Path.Combine(context.VaultDirectory, "notes.vault")));
		Assert.Equal(ownerOnly, File.GetUnixFileMode(Path.Combine(context.DataDirectory, "layout.json")));
		Assert.Equal(ownerOnly | UnixFileMode.UserExecute, File.GetUnixFileMode(context.VaultDirectory));
	}

	[Fact]
	public async Task WriteAsync_ConcurrentWriters_LeaveOneCompleteDocument()
	{
		await using CoreTestContext context = new();
		IAppDataStore store = Store(context);

		await Task.WhenAll(Enumerable.Range(0, 20).Select(i => store.WriteAsync("layout", new Layout(new string('x', i * 50), ThemeMode.Dark, i))));

		Layout? result = await store.ReadAsync<Layout>("layout", Ct);
		Assert.NotNull(result);
		Assert.Equal(result.SidebarWidth * 50, result.ActivePanel.Length);
		Assert.Single(Directory.GetFiles(context.DataDirectory));
	}

	[Fact]
	public async Task ReadAsync_CorruptDocument_IsQuarantinedLoggedAndReadAsNull()
	{
		await using CoreTestContext context = new();
		IAppDataStore store = Store(context);
		string path = Path.Combine(context.DataDirectory, "layout.json");
		await File.WriteAllTextAsync(path, "{ \"activePanel\": ", Encoding.UTF8, Ct);

		Assert.Null(await store.ReadAsync<Layout>("layout", Ct));

		Assert.False(File.Exists(path));
		string quarantined = Path.Combine(context.DataDirectory, "layout.corrupt-20260917T080000000Z.json");
		Assert.True(File.Exists(quarantined));
		Assert.Equal("{ \"activePanel\": ", await File.ReadAllTextAsync(quarantined, Ct));
		Assert.Contains(context.Logs.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("layout", StringComparison.Ordinal));

		await store.WriteAsync("layout", new Layout("fresh", ThemeMode.Dark, 1), Ct);
		Assert.Equal("fresh", (await store.ReadAsync<Layout>("layout", Ct))?.ActivePanel);
	}

	[Fact]
	public async Task ReadAsync_SecondCorruptionAtSameTime_KeepsBothCopies()
	{
		await using CoreTestContext context = new();
		IAppDataStore store = Store(context);
		string path = Path.Combine(context.DataDirectory, "layout.json");

		await File.WriteAllTextAsync(path, "nope", Ct);
		await store.ReadAsync<Layout>("layout", Ct);
		await File.WriteAllTextAsync(path, "still nope", Ct);
		await store.ReadAsync<Layout>("layout", Ct);

		Assert.Equal(2, Directory.GetFiles(context.DataDirectory, "layout.corrupt-*.json").Length);
	}

	[Theory]
	[InlineData("")]
	[InlineData("Settings")]
	[InlineData("../escape")]
	[InlineData("nested/name")]
	[InlineData("nested\\name")]
	[InlineData(".hidden")]
	[InlineData("-dash")]
	[InlineData("with space")]
	[InlineData("settings\n")]
	[InlineData("vault")]
	public async Task Operations_InvalidOrReservedName_Throw(string name)
	{
		await using CoreTestContext context = new();
		IAppDataStore store = Store(context);

		await Assert.ThrowsAnyAsync<ArgumentException>(async () => await store.ReadAsync<Layout>(name, Ct));
		await Assert.ThrowsAnyAsync<ArgumentException>(() => store.WriteAsync(name, new Layout("x", ThemeMode.Dark, 1), Ct));
		await Assert.ThrowsAnyAsync<ArgumentException>(() => store.DeleteAsync(name, Ct));
	}

	[Theory]
	[InlineData("a")]
	[InlineData("settings")]
	[InlineData("ui.layout-2")]
	[InlineData("0123456789012345678901234567890123456789012345678901234567890123")]
	public async Task WriteAsync_ValidName_Succeeds(string name)
	{
		await using CoreTestContext context = new();

		await Store(context).WriteAsync(name, new Layout("x", ThemeMode.Dark, 1), Ct);

		Assert.True(File.Exists(Path.Combine(context.DataDirectory, name + ".json")));
	}

	[Fact]
	public async Task WriteAsync_NameLongerThan64_Throws()
	{
		await using CoreTestContext context = new();

		await Assert.ThrowsAsync<ArgumentException>(() => Store(context).WriteAsync(new string('a', 65), new Layout("x", ThemeMode.Dark, 1), Ct));
	}

	[Fact]
	public async Task DeleteAsync_RemovesDocumentAndIgnoresMissingOnes()
	{
		await using CoreTestContext context = new();
		IAppDataStore store = Store(context);
		await store.WriteAsync("layout", new Layout("x", ThemeMode.Dark, 1), Ct);

		await store.DeleteAsync("layout", Ct);
		await store.DeleteAsync("layout", Ct);

		Assert.Null(await store.ReadAsync<Layout>("layout", Ct));
	}

	private static IAppDataStore Store(CoreTestContext context) => context.Services.GetRequiredService<IAppDataStore>();

	public sealed record Layout(string ActivePanel, ThemeMode Mode, int SidebarWidth);
}
