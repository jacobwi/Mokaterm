using Microsoft.Extensions.DependencyInjection;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Security;
using Mokaterm.Core.Tests.TestSupport;

namespace Mokaterm.Core.Tests.Commands;

public sealed class CommandSnippetStoreTests
{
	private static readonly Guid Login = new("11111111-1111-1111-1111-111111111111");
	private static readonly Guid OtherLogin = new("22222222-2222-2222-2222-222222222222");

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task SaveAsync_RoundTripsThroughTheVault()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		ICommandSnippetStore store = scope.ServiceProvider.GetRequiredService<ICommandSnippetStore>();
		int changes = 0;
		store.Changed += () => changes++;

		CommandSnippet saved = await store.SaveAsync(
			New("  ", "  sudo systemctl restart nginx  ", CommandSnippetScope.Host, host: "  Build-01.Example.COM ", tags: ["nginx", " nginx ", "ops"]),
			Ct);

		Assert.Equal("sudo systemctl restart nginx", saved.Command);
		Assert.Equal("sudo systemctl restart nginx", saved.Name);
		Assert.Equal("build-01.example.com", saved.Host);
		Assert.Equal(["nginx", "ops"], saved.Tags);
		Assert.Equal(context.Time.GetUtcNow(), saved.CreatedAt);
		Assert.Null(saved.LastUsedAt);
		Assert.Equal(0, saved.UseCount);
		Assert.False(saved.RunImmediately);
		Assert.Equal(1, changes);

		// A second scope reads the same encrypted document.
		await using AsyncServiceScope other = await context.UnlockedScopeAsync();
		CommandSnippet reloaded = Assert.Single(await other.ServiceProvider.GetRequiredService<ICommandSnippetStore>().ListAsync(Ct));
		Assert.Equal(saved.Tags, reloaded.Tags);

		// Record equality compares the tag lists by reference, and the reloaded one is a different instance.
		Assert.Equal(saved with { Tags = [] }, reloaded with { Tags = [] });
	}

	[Fact]
	public async Task SaveAsync_SecretsNeverReachAPlainFile()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		ICommandSnippetStore store = scope.ServiceProvider.GetRequiredService<ICommandSnippetStore>();

		await store.SaveAsync(New("Deploy", "deploy --token hunter2", CommandSnippetScope.Global), Ct);

		foreach (string file in Directory.GetFiles(context.DataDirectory, "*", SearchOption.AllDirectories))
		{
			Assert.DoesNotContain("hunter2", await File.ReadAllTextAsync(file, Ct), StringComparison.Ordinal);
		}
	}

	[Fact]
	public async Task SaveAsync_ExistingSnippet_KeepsCreatedAtAndUseCount()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		ICommandSnippetStore store = scope.ServiceProvider.GetRequiredService<ICommandSnippetStore>();
		CommandSnippet saved = await store.SaveAsync(New("Tail", "tail -f /var/log/syslog", CommandSnippetScope.Global), Ct);
		DateTimeOffset createdAt = saved.CreatedAt;
		await store.MarkUsedAsync(saved.Id, Ct);
		context.Time.Advance(TimeSpan.FromMinutes(5));

		CommandSnippet updated = await store.SaveAsync(saved with { Name = "Tail syslog", UseCount = 0, LastUsedAt = null }, Ct);

		Assert.Equal("Tail syslog", updated.Name);
		Assert.Equal(createdAt, updated.CreatedAt);
		Assert.Equal(1, updated.UseCount);
		Assert.Equal(createdAt, updated.LastUsedAt);
		Assert.Single(await store.ListAsync(Ct));
	}

	[Fact]
	public async Task SaveAsync_RejectsMultipleLinesAndUnknownScopes()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		ICommandSnippetStore store = scope.ServiceProvider.GetRequiredService<ICommandSnippetStore>();

		await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(New("Two", "echo one\necho two", CommandSnippetScope.Global), Ct));
		await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(New("Blank", "   ", CommandSnippetScope.Global), Ct));
		await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(New("Host", "ls", CommandSnippetScope.Host, host: null), Ct));
		await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(New("Login", "ls", CommandSnippetScope.Connection, connectionId: Guid.Empty), Ct));
		await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(New("Bad", "ls", (CommandSnippetScope)42), Ct));
		Assert.Empty(await store.ListAsync(Ct));
	}

	[Fact]
	public async Task SaveAsync_GlobalScope_DropsHostAndConnection()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		ICommandSnippetStore store = scope.ServiceProvider.GetRequiredService<ICommandSnippetStore>();

		CommandSnippet saved = await store.SaveAsync(
			New("Uptime", "uptime", CommandSnippetScope.Global, host: "build-01", connectionId: Login),
			Ct);

		Assert.Null(saved.Host);
		Assert.Null(saved.ConnectionId);
	}

	[Fact]
	public async Task QueryAsync_OrdersConnectionThenHostThenGlobal()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		ICommandSnippetStore store = scope.ServiceProvider.GetRequiredService<ICommandSnippetStore>();
		await store.SaveAsync(New("Global", "df -h", CommandSnippetScope.Global), Ct);
		await store.SaveAsync(New("Host", "free -m", CommandSnippetScope.Host, host: "build-01"), Ct);
		await store.SaveAsync(New("Login", "whoami", CommandSnippetScope.Connection, host: "build-01", connectionId: Login), Ct);

		IReadOnlyList<CommandSnippet> applicable = await store.QueryAsync(Target("BUILD-01", Login), Ct);

		Assert.Equal(["Login", "Host", "Global"], applicable.Select(snippet => snippet.Name));
	}

	[Fact]
	public async Task QueryAsync_WithinAScope_PutsTheMostUsedFirst()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		ICommandSnippetStore store = scope.ServiceProvider.GetRequiredService<ICommandSnippetStore>();
		CommandSnippet once = await store.SaveAsync(New("Once", "ls -la", CommandSnippetScope.Global), Ct);
		CommandSnippet twice = await store.SaveAsync(New("Twice", "ps aux", CommandSnippetScope.Global), Ct);
		await store.SaveAsync(New("Never", "id", CommandSnippetScope.Global), Ct);
		await store.MarkUsedAsync(once.Id, Ct);
		await store.MarkUsedAsync(twice.Id, Ct);
		await store.MarkUsedAsync(twice.Id, Ct);

		IReadOnlyList<CommandSnippet> applicable = await store.QueryAsync(Target(null, null), Ct);

		Assert.Equal(["Twice", "Once", "Never"], applicable.Select(snippet => snippet.Name));
	}

	[Fact]
	public async Task QueryAsync_OtherHostsAndLogins_DoNotLeak()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		ICommandSnippetStore store = scope.ServiceProvider.GetRequiredService<ICommandSnippetStore>();
		await store.SaveAsync(New("Everywhere", "uptime", CommandSnippetScope.Global), Ct);
		await store.SaveAsync(New("Build host", "make", CommandSnippetScope.Host, host: "build-01"), Ct);
		await store.SaveAsync(New("Db host", "psql", CommandSnippetScope.Host, host: "db-01"), Ct);
		await store.SaveAsync(New("This login", "whoami", CommandSnippetScope.Connection, host: "build-01", connectionId: Login), Ct);
		await store.SaveAsync(New("Other login", "hostname", CommandSnippetScope.Connection, host: "build-01", connectionId: OtherLogin), Ct);

		Assert.Equal(
			["This login", "Build host", "Everywhere"],
			(await store.QueryAsync(Target("build-01", Login), Ct)).Select(snippet => snippet.Name));

		// A quick-connect session has no saved login, so only the machine and global commands apply.
		Assert.Equal(
			["Build host", "Everywhere"],
			(await store.QueryAsync(Target("build-01", null), Ct)).Select(snippet => snippet.Name));

		Assert.Equal(["Everywhere"], (await store.QueryAsync(Target("unknown-host", null), Ct)).Select(snippet => snippet.Name));
		Assert.Equal(["Everywhere"], (await store.QueryAsync(Target(null, null), Ct)).Select(snippet => snippet.Name));
	}

	[Fact]
	public async Task MarkUsedAsync_CountsAndTimestamps()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		ICommandSnippetStore store = scope.ServiceProvider.GetRequiredService<ICommandSnippetStore>();
		CommandSnippet saved = await store.SaveAsync(New("Logs", "journalctl -xe", CommandSnippetScope.Global), Ct);
		context.Time.Advance(TimeSpan.FromMinutes(3));

		await store.MarkUsedAsync(saved.Id, Ct);
		await store.MarkUsedAsync(Guid.NewGuid(), Ct);

		CommandSnippet used = Assert.Single(await store.ListAsync(Ct));
		Assert.Equal(1, used.UseCount);
		Assert.Equal(context.Time.GetUtcNow(), used.LastUsedAt);
	}

	[Fact]
	public async Task DeleteAsync_RemovesOnlyThatSnippet()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		ICommandSnippetStore store = scope.ServiceProvider.GetRequiredService<ICommandSnippetStore>();
		CommandSnippet first = await store.SaveAsync(New("First", "ls", CommandSnippetScope.Global), Ct);
		await store.SaveAsync(New("Second", "pwd", CommandSnippetScope.Global), Ct);
		int changes = 0;
		store.Changed += () => changes++;

		await store.DeleteAsync(first.Id, Ct);
		await store.DeleteAsync(first.Id, Ct);

		Assert.Equal("Second", Assert.Single(await store.ListAsync(Ct)).Name);
		Assert.Equal(1, changes);
	}

	[Fact]
	public async Task Store_WhileLocked_Throws()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		ICommandSnippetStore store = scope.ServiceProvider.GetRequiredService<ICommandSnippetStore>();
		scope.ServiceProvider.GetRequiredService<IVault>().Lock();

		await Assert.ThrowsAsync<VaultLockedException>(async () => await store.ListAsync(Ct));
		await Assert.ThrowsAsync<VaultLockedException>(async () => await store.QueryAsync(Target("build-01", Login), Ct));
		await Assert.ThrowsAsync<VaultLockedException>(() => store.SaveAsync(New("Locked", "ls", CommandSnippetScope.Global), Ct));
	}

	private static CommandSnippetTarget Target(string? host, Guid? connectionId) => new() { Host = host, ConnectionId = connectionId };

	private static CommandSnippet New(
		string name,
		string command,
		CommandSnippetScope scope,
		string? host = "build-01",
		Guid? connectionId = null,
		IReadOnlyList<string>? tags = null) => new()
		{
			Id = Guid.NewGuid(),
			Name = name,
			Command = command,
			Scope = scope,
			Host = host,
			ConnectionId = connectionId ?? (scope == CommandSnippetScope.Connection ? Login : null),
			Tags = tags ?? [],
		};
}
