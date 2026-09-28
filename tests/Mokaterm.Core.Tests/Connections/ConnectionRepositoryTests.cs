using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Core.Tests.TestSupport;

namespace Mokaterm.Core.Tests.Connections;

public sealed class ConnectionRepositoryTests
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task SaveHostAndConnection_AppearInCatalogTrimmedAndStamped()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		IConnectionRepository repository = Repository(scope);
		int changes = 0;
		repository.Changed += () => changes++;
		HostProfile host = Host() with { Name = "  Web 1 ", Address = " 10.10.2.3 ", Tags = [" prod ", "", "Prod", "db"], Notes = "   " };
		ConnectionProfile connection = Connection(host.Id) with
		{
			ProtocolId = " SSH ",
			Username = " abc ",
			Label = " ",
			Options = ProtocolOptions.Empty.With("keepAlive", 30),
			Terminal = new TerminalProfileOverrides(),
		};

		await repository.SaveHostAsync(host, Ct);
		await repository.SaveConnectionAsync(connection, Ct);

		ConnectionCatalog catalog = await repository.GetCatalogAsync(Ct);
		HostProfile savedHost = Assert.Single(catalog.Hosts);
		Assert.Equal("Web 1", savedHost.Name);
		Assert.Equal("10.10.2.3", savedHost.Address);
		Assert.Equal("prod,db", string.Join(',', savedHost.Tags));
		Assert.Null(savedHost.Notes);
		Assert.Equal(context.Time.GetUtcNow(), savedHost.CreatedAt);
		ConnectionProfile savedConnection = Assert.Single(catalog.Connections);
		Assert.Equal("ssh", savedConnection.ProtocolId);
		Assert.Equal("abc", savedConnection.Username);
		Assert.Null(savedConnection.Label);
		Assert.Null(savedConnection.Terminal);
		Assert.Equal(30, savedConnection.Options.GetInt32("keepAlive", 0));
		Assert.Equal("abc@10.10.2.3", savedConnection.GetTitle(savedHost));
		Assert.Equal(2, changes);
	}

	[Fact]
	public async Task SaveHostAsync_Update_KeepsCreatedAtAndBumpsUpdatedAt()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		IConnectionRepository repository = Repository(scope);
		HostProfile host = Host();
		await repository.SaveHostAsync(host, Ct);
		DateTimeOffset createdAt = context.Time.GetUtcNow();

		context.Time.Advance(TimeSpan.FromMinutes(5));
		await repository.SaveHostAsync(host with { Name = "Renamed", CreatedAt = default }, Ct);

		HostProfile saved = Assert.Single((await repository.GetCatalogAsync(Ct)).Hosts);
		Assert.Equal("Renamed", saved.Name);
		Assert.Equal(createdAt, saved.CreatedAt);
		Assert.Equal(context.Time.GetUtcNow(), saved.UpdatedAt);
	}

	[Fact]
	public async Task SaveAsync_InvalidInput_ThrowsAndChangesNothing()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		IConnectionRepository repository = Repository(scope);
		HostProfile host = Host();
		await repository.SaveHostAsync(host, Ct);

		await Assert.ThrowsAnyAsync<ArgumentException>(() => repository.SaveHostAsync(Host() with { Address = "  " }, Ct));
		await Assert.ThrowsAnyAsync<ArgumentException>(() => repository.SaveHostAsync(Host() with { Id = Guid.Empty }, Ct));
		await Assert.ThrowsAnyAsync<ArgumentException>(() => repository.SaveHostAsync(Host() with { FolderId = Guid.NewGuid() }, Ct));
		await Assert.ThrowsAnyAsync<ArgumentException>(() => repository.SaveConnectionAsync(Connection(host.Id) with { ProtocolId = " " }, Ct));
		await Assert.ThrowsAnyAsync<ArgumentException>(() => repository.SaveConnectionAsync(Connection(host.Id) with { Port = 0 }, Ct));
		await Assert.ThrowsAnyAsync<ArgumentException>(() => repository.SaveConnectionAsync(Connection(host.Id) with { Port = 65536 }, Ct));
		await Assert.ThrowsAnyAsync<ArgumentException>(() => repository.SaveConnectionAsync(Connection(Guid.NewGuid()), Ct));
		await Assert.ThrowsAnyAsync<ArgumentException>(() => repository.SaveConnectionAsync(
			Connection(host.Id) with { Terminal = new TerminalProfileOverrides { FontFamily = "mono} body { display: none" } },
			Ct));
		await Assert.ThrowsAnyAsync<ArgumentException>(() => repository.SaveFolderAsync(Folder("Missing parent") with { ParentId = Guid.NewGuid() }, Ct));
		await Assert.ThrowsAnyAsync<ArgumentException>(() => repository.SaveFolderAsync(Folder(" "), Ct));

		await repository.SaveConnectionAsync(Connection(host.Id) with { Port = 65535 }, Ct);
		ConnectionCatalog catalog = await repository.GetCatalogAsync(Ct);
		Assert.Single(catalog.Hosts);
		Assert.Single(catalog.Connections);
		Assert.Empty(catalog.Folders);
	}

	[Fact]
	public async Task SaveFolderAsync_MovingIntoItselfOrADescendant_IsRejected()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		IConnectionRepository repository = Repository(scope);
		ConnectionFolder root = Folder("Root");
		ConnectionFolder child = Folder("Child") with { ParentId = root.Id };
		ConnectionFolder grandchild = Folder("Grandchild") with { ParentId = child.Id };
		await repository.SaveFolderAsync(root, Ct);
		await repository.SaveFolderAsync(child, Ct);
		await repository.SaveFolderAsync(grandchild, Ct);

		await Assert.ThrowsAsync<ArgumentException>(() => repository.SaveFolderAsync(root with { ParentId = root.Id }, Ct));
		await Assert.ThrowsAsync<ArgumentException>(() => repository.SaveFolderAsync(root with { ParentId = grandchild.Id }, Ct));

		await repository.SaveFolderAsync(grandchild with { ParentId = root.Id }, Ct);
		Assert.Equal(root.Id, (await repository.GetCatalogAsync(Ct)).FindFolder(grandchild.Id)?.ParentId);
	}

	[Fact]
	public async Task DeleteFolderAsync_MovesChildFoldersAndHostsToParent()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		IConnectionRepository repository = Repository(scope);
		ConnectionFolder root = Folder("Root");
		ConnectionFolder middle = Folder("Middle") with { ParentId = root.Id };
		ConnectionFolder leaf = Folder("Leaf") with { ParentId = middle.Id };
		HostProfile host = Host() with { FolderId = middle.Id };
		await repository.SaveFolderAsync(root, Ct);
		await repository.SaveFolderAsync(middle, Ct);
		await repository.SaveFolderAsync(leaf, Ct);
		await repository.SaveHostAsync(host, Ct);

		await repository.DeleteFolderAsync(middle.Id, Ct);

		ConnectionCatalog catalog = await repository.GetCatalogAsync(Ct);
		Assert.Null(catalog.FindFolder(middle.Id));
		Assert.Equal(root.Id, catalog.FindFolder(leaf.Id)?.ParentId);
		Assert.Equal(root.Id, catalog.FindHost(host.Id)?.FolderId);
	}

	[Fact]
	public async Task DeleteHostAsync_RemovesConnectionsAndTheirPrivateCredentialsOnly()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		IConnectionRepository repository = Repository(scope);
		ICredentialStore credentials = scope.ServiceProvider.GetRequiredService<ICredentialStore>();
		HostProfile host = Host();
		HostProfile otherHost = Host();
		ConnectionProfile ssh = Connection(host.Id);
		ConnectionProfile ftp = Connection(host.Id) with { ProtocolId = "ftp" };
		ConnectionProfile otherConnection = Connection(otherHost.Id);
		await repository.SaveHostAsync(host, Ct);
		await repository.SaveHostAsync(otherHost, Ct);
		await repository.SaveConnectionAsync(ssh, Ct);
		await repository.SaveConnectionAsync(ftp, Ct);
		await repository.SaveConnectionAsync(otherConnection, Ct);
		CredentialInfo sshPrivate = await SavePrivateCredentialAsync(credentials, ssh.Id);
		CredentialInfo ftpPrivate = await SavePrivateCredentialAsync(credentials, ftp.Id);
		CredentialInfo otherPrivate = await SavePrivateCredentialAsync(credentials, otherConnection.Id);
		CredentialInfo shared = await credentials.SaveAsync(
			new CredentialInfo { Id = Guid.NewGuid(), Name = "Shared", Kind = CredentialKind.Password, IsShared = true, OwnerConnectionId = ssh.Id },
			new CredentialSecretInput { Password = "shared" },
			Ct);

		await repository.DeleteHostAsync(host.Id, Ct);

		ConnectionCatalog catalog = await repository.GetCatalogAsync(Ct);
		Assert.Equal(otherHost.Id, Assert.Single(catalog.Hosts).Id);
		Assert.Equal(otherConnection.Id, Assert.Single(catalog.Connections).Id);
		Assert.Null(await credentials.FindAsync(sshPrivate.Id, Ct));
		Assert.Null(await credentials.FindAsync(ftpPrivate.Id, Ct));
		Assert.NotNull(await credentials.FindAsync(otherPrivate.Id, Ct));
		Assert.NotNull(await credentials.FindAsync(shared.Id, Ct));
	}

	[Fact]
	public async Task DeleteConnectionAsync_RemovesItsPrivateCredentialAndKeepsSharedOnes()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		IConnectionRepository repository = Repository(scope);
		ICredentialStore credentials = scope.ServiceProvider.GetRequiredService<ICredentialStore>();
		HostProfile host = Host();
		ConnectionProfile connection = Connection(host.Id);
		await repository.SaveHostAsync(host, Ct);
		CredentialInfo owned = await SavePrivateCredentialAsync(credentials, connection.Id);
		CredentialInfo shared = await credentials.SaveAsync(
			new CredentialInfo { Id = Guid.NewGuid(), Name = "Shared", Kind = CredentialKind.Password, IsShared = true },
			new CredentialSecretInput { Password = "shared" },
			Ct);
		await repository.SaveConnectionAsync(connection with { CredentialId = owned.Id }, Ct);
		int changes = 0;
		repository.Changed += () => changes++;

		await repository.DeleteConnectionAsync(connection.Id, Ct);
		await repository.DeleteConnectionAsync(connection.Id, Ct);

		Assert.Empty((await repository.GetCatalogAsync(Ct)).Connections);
		Assert.Single((await repository.GetCatalogAsync(Ct)).Hosts);
		Assert.Null(await credentials.FindAsync(owned.Id, Ct));
		Assert.NotNull(await credentials.FindAsync(shared.Id, Ct));
		Assert.Equal(1, changes);
	}

	[Fact]
	public async Task MarkConnectedAsync_SetsLastConnectedAtWhichLaterSavesKeep()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		IConnectionRepository repository = Repository(scope);
		HostProfile host = Host();
		ConnectionProfile connection = Connection(host.Id);
		await repository.SaveHostAsync(host, Ct);
		await repository.SaveConnectionAsync(connection, Ct);
		DateTimeOffset connectedAt = context.Time.GetUtcNow().AddMinutes(3);

		await repository.MarkConnectedAsync(connection.Id, connectedAt, Ct);
		await repository.SaveConnectionAsync(connection with { Username = "changed" }, Ct);
		await repository.MarkConnectedAsync(Guid.NewGuid(), connectedAt, Ct);

		ConnectionProfile saved = Assert.Single((await repository.GetCatalogAsync(Ct)).Connections);
		Assert.Equal(connectedAt, saved.LastConnectedAt);
		Assert.Equal("changed", saved.Username);
	}

	[Fact]
	public async Task GetCatalogAsync_AfterLock_ThrowsAndReloadsAfterUnlock()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		IConnectionRepository repository = Repository(scope);
		IVault vault = scope.ServiceProvider.GetRequiredService<IVault>();
		HostProfile host = Host();
		await repository.SaveHostAsync(host, Ct);
		await repository.GetCatalogAsync(Ct);

		vault.Lock();

		await Assert.ThrowsAsync<VaultLockedException>(async () => await repository.GetCatalogAsync(Ct));
		await Assert.ThrowsAsync<VaultLockedException>(() => repository.SaveHostAsync(Host(), Ct));
		Assert.True((await vault.UnlockAsync(CoreTestContext.MasterPassword, Ct)).Succeeded);
		Assert.Equal(host.Id, Assert.Single((await repository.GetCatalogAsync(Ct)).Hosts).Id);
	}

	[Fact]
	public async Task ChangesFromAnotherScope_RaiseChangedAndRefreshTheCache()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope first = await context.CreateVaultScopeAsync();
		await using AsyncServiceScope second = await context.UnlockedScopeAsync();
		IConnectionRepository firstRepository = Repository(first);
		IConnectionRepository secondRepository = Repository(second);
		Assert.Empty((await secondRepository.GetCatalogAsync(Ct)).Hosts);
		int secondChanges = 0;
		secondRepository.Changed += () => secondChanges++;

		await firstRepository.SaveHostAsync(Host(), Ct);

		Assert.Equal(1, secondChanges);
		Assert.Single((await secondRepository.GetCatalogAsync(Ct)).Hosts);

		await secondRepository.SaveHostAsync(Host(), Ct);
		Assert.Equal(2, (await firstRepository.GetCatalogAsync(Ct)).Hosts.Count);
	}

	[Fact]
	public async Task ConcurrentSavesFromTwoScopes_KeepEveryHost()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope first = await context.CreateVaultScopeAsync();
		await using AsyncServiceScope second = await context.UnlockedScopeAsync();
		IConnectionRepository firstRepository = Repository(first);
		IConnectionRepository secondRepository = Repository(second);
		await firstRepository.GetCatalogAsync(Ct);
		await secondRepository.GetCatalogAsync(Ct);

		await Task.WhenAll(Enumerable.Range(0, 10).Select(i => (i % 2 == 0 ? firstRepository : secondRepository).SaveHostAsync(Host())));

		Assert.Equal(10, (await firstRepository.GetCatalogAsync(Ct)).Hosts.Count);
		Assert.Equal(10, (await secondRepository.GetCatalogAsync(Ct)).Hosts.Count);
	}

	[Fact]
	public async Task ConnectionsFile_DoesNotContainAddressesOrUsernames()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		IConnectionRepository repository = Repository(scope);
		HostProfile host = Host() with { Address = "db-internal-7731.example.net" };
		await repository.SaveHostAsync(host, Ct);
		await repository.SaveConnectionAsync(Connection(host.Id) with { Username = "operator-5512" }, Ct);

		byte[] raw = await File.ReadAllBytesAsync(Path.Combine(context.VaultDirectory, "connections.vault"), Ct);

		Assert.Equal(-1, raw.AsSpan().IndexOf(Encoding.UTF8.GetBytes("db-internal-7731")));
		Assert.Equal(-1, raw.AsSpan().IndexOf(Encoding.UTF8.GetBytes("operator-5512")));
	}

	[Fact]
	public async Task DeleteConnectionAsync_MovesItsSavedCommandsToTheMachine()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		IConnectionRepository repository = Repository(scope);
		ICommandSnippetStore commands = scope.ServiceProvider.GetRequiredService<ICommandSnippetStore>();
		HostProfile host = Host();
		ConnectionProfile connection = Connection(host.Id);
		await repository.SaveHostAsync(host, Ct);
		await repository.SaveConnectionAsync(connection, Ct);
		await commands.SaveAsync(Snippet("Restart", CommandSnippetScope.Connection, host.Address, connection.Id), Ct);
		await commands.SaveAsync(Snippet("Anywhere", CommandSnippetScope.Global), Ct);

		await repository.DeleteConnectionAsync(connection.Id, Ct);

		IReadOnlyList<CommandSnippet> left = await commands.ListAsync(Ct);
		CommandSnippet moved = Assert.Single(left, snippet => snippet.Name == "Restart");
		Assert.Equal(CommandSnippetScope.Host, moved.Scope);
		Assert.Null(moved.ConnectionId);
		Assert.Equal(host.Address, moved.Host);
		Assert.Contains(left, snippet => snippet.Name == "Anywhere");

		// The machine is still there, so the command still shows up in a session to it.
		Assert.Contains(
			await commands.QueryAsync(new CommandSnippetTarget { Host = host.Address, ConnectionId = Guid.NewGuid() }, Ct),
			snippet => snippet.Name == "Restart");
	}

	[Fact]
	public async Task DeleteHostAsync_DeletesTheCommandsSavedOnIt()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		IConnectionRepository repository = Repository(scope);
		ICommandSnippetStore commands = scope.ServiceProvider.GetRequiredService<ICommandSnippetStore>();
		HostProfile host = Host();
		HostProfile other = Host() with { Address = "10.10.2.9" };
		ConnectionProfile connection = Connection(host.Id);
		await repository.SaveHostAsync(host, Ct);
		await repository.SaveHostAsync(other, Ct);
		await repository.SaveConnectionAsync(connection, Ct);
		await commands.SaveAsync(Snippet("Login", CommandSnippetScope.Connection, host.Address, connection.Id), Ct);
		await commands.SaveAsync(Snippet("Machine", CommandSnippetScope.Host, host.Address), Ct);
		await commands.SaveAsync(Snippet("Other machine", CommandSnippetScope.Host, other.Address), Ct);
		await commands.SaveAsync(Snippet("Anywhere", CommandSnippetScope.Global), Ct);

		await repository.DeleteHostAsync(host.Id, Ct);

		IReadOnlyList<CommandSnippet> left = await commands.ListAsync(Ct);
		Assert.Equal(["Anywhere", "Other machine"], [.. left.Select(snippet => snippet.Name).Order(StringComparer.Ordinal)]);
	}

	[Fact]
	public async Task SaveHostAsync_NewAddress_TakesItsSavedCommandsAlong()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		IConnectionRepository repository = Repository(scope);
		ICommandSnippetStore commands = scope.ServiceProvider.GetRequiredService<ICommandSnippetStore>();
		HostProfile host = Host();
		await repository.SaveHostAsync(host, Ct);
		await commands.SaveAsync(Snippet("Machine", CommandSnippetScope.Host, host.Address), Ct);

		await repository.SaveHostAsync(host with { Address = "build-01.example.com" }, Ct);

		Assert.Equal("build-01.example.com", Assert.Single(await commands.ListAsync(Ct)).Host);
		Assert.Contains(
			await commands.QueryAsync(new CommandSnippetTarget { Host = "build-01.example.com" }, Ct),
			snippet => snippet.Name == "Machine");
	}

	private static CommandSnippet Snippet(string name, CommandSnippetScope scope, string? host = null, Guid? connectionId = null) => new()
	{
		Id = Guid.NewGuid(),
		Name = name,
		Command = "uptime",
		Scope = scope,
		Host = host,
		ConnectionId = connectionId,
	};

	private static IConnectionRepository Repository(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<IConnectionRepository>();

	private static HostProfile Host() => new() { Id = Guid.NewGuid(), Address = "10.10.2.3" };

	private static ConnectionProfile Connection(Guid hostId) => new()
	{
		Id = Guid.NewGuid(),
		HostId = hostId,
		ProtocolId = "ssh",
		Username = "abc",
		AuthenticationMethod = AuthenticationMethod.Password,
	};

	private static ConnectionFolder Folder(string name) => new() { Id = Guid.NewGuid(), Name = name };

	private static Task<CredentialInfo> SavePrivateCredentialAsync(ICredentialStore credentials, Guid connectionId) =>
		credentials.SaveAsync(
			new CredentialInfo { Id = Guid.NewGuid(), Name = "Private", Kind = CredentialKind.Password, OwnerConnectionId = connectionId },
			new CredentialSecretInput { Password = "private" }).AsTask();
}
