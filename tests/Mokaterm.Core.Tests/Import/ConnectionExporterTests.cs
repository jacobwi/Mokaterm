using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Import;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Core.Import;
using Mokaterm.Core.Tests.TestSupport;

namespace Mokaterm.Core.Tests.Import;

public sealed class ConnectionExporterTests
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task ExportAsync_WritesEveryLoginWithItsFolderPath()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		IConnectionRepository repository = scope.ServiceProvider.GetRequiredService<IConnectionRepository>();
		await SeedAsync(repository);

		ConnectionExport export = await Exporter(scope).ExportAsync(Ct);

		Assert.Equal(2, export.Logins);
		Assert.Equal(1, export.Hosts);
		Assert.Equal(1, export.Folders);
		Assert.Equal(1, export.HostsWithoutLogin);
		Assert.EndsWith(".json", export.FileName, StringComparison.Ordinal);

		string json = Encoding.UTF8.GetString(export.Content);
		Assert.Contains("\"lab/build\"", json.ToLowerInvariant(), StringComparison.Ordinal);
		Assert.Contains("build-01.example.com", json, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ExportAsync_CarriesNoSecret()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		IConnectionRepository repository = scope.ServiceProvider.GetRequiredService<IConnectionRepository>();
		await SeedAsync(repository);

		ConnectionExport export = await Exporter(scope).ExportAsync(Ct);

		// The login's method is written, so "password" shows up as a value; what must not is a field holding one.
		string json = Encoding.UTF8.GetString(export.Content).ToLowerInvariant();
		Assert.DoesNotContain("\"password\":", json, StringComparison.Ordinal);
		Assert.DoesNotContain("\"passphrase\":", json, StringComparison.Ordinal);
		Assert.DoesNotContain("credentialid", json, StringComparison.Ordinal);
		Assert.DoesNotContain("privatekey", json, StringComparison.Ordinal);
	}

	[Fact]
	public async Task AnExport_ReadsBackWithTheHostAsItWasSaved()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		IConnectionRepository repository = scope.ServiceProvider.GetRequiredService<IConnectionRepository>();
		await SeedAsync(repository);
		ConnectionExport export = await Exporter(scope).ExportAsync(Ct);

		ConnectionBundleImportSource source = new();
		ImportPreview preview = await source.ReadAsync(new ImportReadRequest(new ImportFile { Name = "export.json", Content = export.Content }), Ct);

		Assert.Equal(ImportAvailability.Found, preview.Availability);
		ImportedEntry entry = Assert.Single(preview.Entries, candidate => candidate.ProtocolId == "ssh");
		Assert.Equal("build-01.example.com", entry.Address);
		Assert.Equal("Build server", entry.Name);
		Assert.Equal("ssh", entry.ProtocolId);
		Assert.Equal(2222, entry.Port);
		Assert.Equal("Lab/Build", entry.FolderPath);
		Assert.Equal(["ci", "linux"], entry.Tags);
		Assert.Equal(HostEnvironment.Production, entry.Environment);
		Assert.Equal("#ef5350", entry.Color);
		Assert.Equal("Deploy user", entry.Label);
		Assert.Equal("keep", entry.Options.GetString("ssh.startupCommand"));
	}

	[Fact]
	public async Task AnExport_ImportsIntoAnEmptyVaultAsItWas()
	{
		await using CoreTestContext source = new();
		byte[] content;
		await using (AsyncServiceScope scope = await source.CreateVaultScopeAsync())
		{
			IConnectionRepository repository = scope.ServiceProvider.GetRequiredService<IConnectionRepository>();
			await SeedAsync(repository);
			content = (await Exporter(scope).ExportAsync(Ct)).Content;
		}

		await using CoreTestContext target = new();
		await using AsyncServiceScope targetScope = await target.CreateVaultScopeAsync();
		IConnectionImporter importer = targetScope.ServiceProvider.GetRequiredService<IConnectionImporter>();
		ImportPreview preview = await importer.PreviewAsync(
			ConnectionBundleImportSource.SourceId,
			new ImportReadRequest(new ImportFile { Name = "export.json", Content = content }),
			Ct);

		ImportResult result = await importer.ImportAsync(new ImportRequest { Entries = preview.Entries }, Ct);

		Assert.Null(result.Error);
		Assert.Equal(2, result.LoginsCreated);
		Assert.Equal(1, result.HostsCreated);
		Assert.Equal(2, result.FoldersCreated);

		IConnectionRepository targetRepository = targetScope.ServiceProvider.GetRequiredService<IConnectionRepository>();
		ConnectionCatalog catalog = await targetRepository.GetCatalogAsync(Ct);
		HostProfile host = Assert.Single(catalog.Hosts);
		Assert.Equal("build-01.example.com", host.Address);
		Assert.Equal(["ci", "linux"], host.Tags);
		Assert.Equal(HostEnvironment.Production, host.Environment);
		Assert.Equal("#ef5350", host.Color);
		Assert.Contains(catalog.Connections, login => login.Username == "deploy" && login.Label == "Deploy user" && login.Port == 2222);

		// No credential travelled with the file, so nothing points at one.
		Assert.All(catalog.Connections, login => Assert.Null(login.CredentialId));
	}

	[Fact]
	public async Task ReadAsync_AFileThatIsNotAnExport_SaysSoInsteadOfThrowing()
	{
		ConnectionBundleImportSource source = new();

		ImportPreview notJson = await source.ReadAsync(new ImportReadRequest(new ImportFile { Name = "x.json", Content = Encoding.UTF8.GetBytes("nonsense") }), Ct);
		ImportPreview otherJson = await source.ReadAsync(new ImportReadRequest(new ImportFile { Name = "x.json", Content = Encoding.UTF8.GetBytes("{\"kind\":\"something-else\"}") }), Ct);
		ImportPreview nothingPicked = await source.ReadAsync(new ImportReadRequest(), Ct);

		Assert.Equal(ImportAvailability.Unreadable, notJson.Availability);
		Assert.Equal(ImportAvailability.Unreadable, otherJson.Availability);
		Assert.Equal(ImportAvailability.NotFound, nothingPicked.Availability);
	}

	private static ConnectionExporter Exporter(AsyncServiceScope scope) =>
		new(scope.ServiceProvider.GetRequiredService<IConnectionRepository>(), TimeProvider.System);

	private static async Task SeedAsync(IConnectionRepository repository)
	{
		Guid lab = Guid.NewGuid();
		Guid build = Guid.NewGuid();
		await repository.SaveFolderAsync(new ConnectionFolder { Id = lab, Name = "Lab" }, Ct);
		await repository.SaveFolderAsync(new ConnectionFolder { Id = build, Name = "Build", ParentId = lab }, Ct);

		Guid hostId = Guid.NewGuid();
		await repository.SaveHostAsync(
			new HostProfile
			{
				Id = hostId,
				Name = "Build server",
				Address = "build-01.example.com",
				FolderId = build,
				Tags = ["ci", "linux"],
				Environment = HostEnvironment.Production,
				Color = "#ef5350",
			},
			Ct);

		// A machine with no login has nothing to connect with, so it is counted and left out.
		await repository.SaveHostAsync(new HostProfile { Id = Guid.NewGuid(), Address = "spare.example.com" }, Ct);

		await repository.SaveConnectionAsync(
			new ConnectionProfile
			{
				Id = Guid.NewGuid(),
				HostId = hostId,
				ProtocolId = "ssh",
				Port = 2222,
				Username = "deploy",
				Label = "Deploy user",
				CredentialId = Guid.NewGuid(),
				Options = ProtocolOptions.Empty.With("ssh.startupCommand", "keep"),
			},
			Ct);

		await repository.SaveConnectionAsync(
			new ConnectionProfile
			{
				Id = Guid.NewGuid(),
				HostId = hostId,
				ProtocolId = "sftp",
				Username = "deploy",
			},
			Ct);
	}
}
