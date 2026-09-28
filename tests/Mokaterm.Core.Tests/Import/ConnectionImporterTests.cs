using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Import;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Core.Import;
using Mokaterm.Core.Tests.TestSupport;

namespace Mokaterm.Core.Tests.Import;

public sealed class ConnectionImporterTests
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task PreviewAsync_MarksEntriesThatMatchASavedLoginOnAddressUserAndProtocol()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		IConnectionRepository repository = scope.ServiceProvider.GetRequiredService<IConnectionRepository>();
		Guid hostId = Guid.NewGuid();
		await repository.SaveHostAsync(new HostProfile { Id = hostId, Address = "10.0.0.1" }, Ct);
		await repository.SaveConnectionAsync(new ConnectionProfile { Id = Guid.NewGuid(), HostId = hostId, ProtocolId = "ssh", Username = "deploy" }, Ct);

		ConnectionImporter importer = Importer(scope, Entry("same", "10.0.0.1", "deploy"), Entry("other-user", "10.0.0.1", "root"), Entry("other-host", "10.0.0.2", "deploy"));
		ImportPreview preview = await importer.PreviewAsync("fake", new ImportReadRequest(), Ct);

		Assert.True(Assert.Single(preview.Entries, entry => entry.Name == "same").AlreadyExists);
		Assert.False(Assert.Single(preview.Entries, entry => entry.Name == "other-user").AlreadyExists);
		Assert.False(Assert.Single(preview.Entries, entry => entry.Name == "other-host").AlreadyExists);
	}

	[Fact]
	public async Task PreviewAsync_SkipsEntriesWhoseProtocolNoModuleRegistered()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();

		ConnectionImporter importer = Importer(
			scope,
			new FakeProtocolRegistry("ssh"),
			Entry("box", "10.0.0.1", "deploy"),
			Entry("mail", "10.0.0.3", null) with { ProtocolId = "telnet" });

		ImportPreview preview = await importer.PreviewAsync("fake", new ImportReadRequest(), Ct);

		Assert.Equal("box", Assert.Single(preview.Entries).Name);
		Assert.Contains(preview.Skipped, skip => skip.Name == "mail" && skip.Reason.Contains("telnet", StringComparison.Ordinal));
	}

	[Fact]
	public async Task PreviewAsync_ReportsAnUnknownSourceInsteadOfThrowing()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();

		ImportPreview preview = await Importer(scope).PreviewAsync("nope", new ImportReadRequest(), Ct);

		Assert.Equal(ImportAvailability.Unreadable, preview.Availability);
	}

	[Fact]
	public async Task ImportAsync_CreatesTheFolderHostsAndLogins()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		IConnectionRepository repository = scope.ServiceProvider.GetRequiredService<IConnectionRepository>();

		ImportResult result = await Importer(scope).ImportAsync(
			new ImportRequest
			{
				Entries =
				[
					Entry("web", "10.0.0.1", "deploy") with { FolderPath = "prod", Port = 2222 },
					Entry("db", "10.0.0.9", "postgres") with { FolderPath = "prod/data", IdentityFilePath = @"C:\keys\db.ppk" },
				],
				NewFolderName = "Imported",
			},
			Ct);

		Assert.Null(result.Error);
		Assert.Equal(2, result.HostsCreated);
		Assert.Equal(2, result.LoginsCreated);

		// "Imported", "prod" and "prod/data" are three folders.
		Assert.Equal(3, result.FoldersCreated);

		ConnectionCatalog catalog = await repository.GetCatalogAsync(Ct);
		ConnectionFolder imported = Assert.Single(catalog.Folders, folder => folder.Name == "Imported");
		ConnectionFolder prod = Assert.Single(catalog.Folders, folder => folder.Name == "prod");
		Assert.Equal(imported.Id, prod.ParentId);
		HostProfile db = Assert.Single(catalog.Hosts, host => host.Address == "10.0.0.9");
		Assert.Contains(@"C:\keys\db.ppk", db.Notes, StringComparison.Ordinal);
		ConnectionProfile web = Assert.Single(catalog.Connections, login => login.Port == 2222);
		Assert.Equal("deploy", web.Username);
		Assert.Equal(AuthenticationMethod.Password, web.AuthenticationMethod);

		// An import never brings a secret, so no login points at a credential.
		Assert.All(catalog.Connections, login => Assert.Null(login.CredentialId));
	}

	[Fact]
	public async Task ImportAsync_AddsLoginsToAHostThatIsAlreadySavedInsteadOfDoublingIt()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		IConnectionRepository repository = scope.ServiceProvider.GetRequiredService<IConnectionRepository>();
		Guid hostId = Guid.NewGuid();
		await repository.SaveHostAsync(new HostProfile { Id = hostId, Name = "Web", Address = "10.0.0.1" }, Ct);

		ImportResult result = await Importer(scope).ImportAsync(
			new ImportRequest { Entries = [Entry("web", "10.0.0.1", "deploy"), Entry("web-root", "10.0.0.1", "root")] },
			Ct);

		Assert.Equal(0, result.HostsCreated);
		Assert.Equal(2, result.HostsReused);
		ConnectionCatalog catalog = await repository.GetCatalogAsync(Ct);
		Assert.Equal("Web", Assert.Single(catalog.Hosts).Name);
		Assert.Equal(2, catalog.Connections.Count);
	}

	[Fact]
	public async Task ImportAsync_DoesNothingForAnEmptySelection()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();

		ImportResult result = await Importer(scope).ImportAsync(new ImportRequest { Entries = [] }, Ct);

		Assert.False(result.CreatedAnything);
	}

	private static ImportedEntry Entry(string name, string address, string? username) => new()
	{
		Key = "fake:" + name,
		Name = name,
		Address = address,
		ProtocolId = "ssh",
		Username = username,
	};

	private static ConnectionImporter Importer(AsyncServiceScope scope, params ImportedEntry[] entries) =>
		Importer(scope, protocols: null, entries);

	private static ConnectionImporter Importer(AsyncServiceScope scope, IProtocolRegistry? protocols, params ImportedEntry[] entries) =>
		new ConnectionImporter(
			[new FakeSource(entries)],
			scope.ServiceProvider.GetRequiredService<IConnectionRepository>(),
			scope.ServiceProvider.GetRequiredService<ILogger<ConnectionImporter>>(),
			protocols);

	private sealed class FakeSource : IConnectionImportSource
	{
		private readonly ImportedEntry[] _entries;

		public FakeSource(ImportedEntry[] entries) => _entries = entries;

		public ImportSourceInfo Info { get; } = new() { Id = "fake", DisplayName = "Fake" };

		public ValueTask<ImportPreview> ReadAsync(ImportReadRequest request, CancellationToken cancellationToken) =>
			ValueTask.FromResult(new ImportPreview
			{
				SourceId = "fake",
				Availability = _entries.Length == 0 ? ImportAvailability.NotFound : ImportAvailability.Found,
				Entries = _entries,
			});
	}

	private sealed class FakeProtocolRegistry : IProtocolRegistry
	{
		private readonly HashSet<string> _known;

		public FakeProtocolRegistry(params string[] known) => _known = new HashSet<string>(known, StringComparer.OrdinalIgnoreCase);

		public IReadOnlyList<ProtocolDescriptor> Protocols => [];

		public IProtocolProvider? Find(string protocolId) => _known.Contains(protocolId) ? new FakeProvider(protocolId) : null;

		public IProtocolProvider Get(string protocolId) => Find(protocolId) ?? throw new KeyNotFoundException(protocolId);

		private sealed class FakeProvider : IProtocolProvider
		{
			public FakeProvider(string protocolId) => Descriptor = new ProtocolDescriptor
			{
				Id = protocolId,
				DisplayName = protocolId,
				DefaultPort = 22,
				Capabilities = ProtocolCapabilities.Terminal,
			};

			public ProtocolDescriptor Descriptor { get; }

			public Task<IProtocolSession> ConnectAsync(ProtocolConnectContext context, CancellationToken cancellationToken) =>
				throw new NotSupportedException();
		}
	}
}
