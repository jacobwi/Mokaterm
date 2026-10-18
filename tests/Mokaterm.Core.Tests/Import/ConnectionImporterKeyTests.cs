using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Import;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Core.Import;
using Mokaterm.Core.Keys;
using Mokaterm.Core.Tests.Keys;
using Mokaterm.Core.Tests.TestSupport;

namespace Mokaterm.Core.Tests.Import;

/// <summary>
/// Copying the key files imported logins name into the keychain: what the preview says about each, one keychain entry per
/// key however it is reached, and nothing at all read on the web host.
/// </summary>
public sealed class ConnectionImporterKeyTests : IAsyncDisposable
{
	private const string Passphrase = "correct horse";

	private readonly TempFolder _home = new("mokaterm-import-keys");

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task PreviewAsync_SaysWhatHappensToEachKey()
	{
		WriteKey("id_ed25519", ReferenceKeys.Ed25519);
		WriteKey("id_locked", ReferenceKeys.EncryptedEd25519);
		WriteKey("notes.txt", "not a key at all");
		WriteKey("old.pub", ReferenceKeys.Ed25519PublicKey);
		await using CoreTestContext context = Context();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		ConnectionImporter importer = Importer(
			scope,
			HostKind.Desktop,
			Entry("open", "~/.ssh/id_ed25519"),
			Entry("again", "%d/.ssh/id_ed25519"),
			Entry("locked", "~/.ssh/id_locked"),
			Entry("gone", "~/.ssh/id_gone"),
			Entry("text", "~/.ssh/notes.txt"),
			Entry("public", "~/.ssh/old.pub"),
			Entry("token", "~/.ssh/%C"),
			Entry("keyless", null));

		Assert.True(importer.CanImportKeys);
		ImportPreview preview = await importer.PreviewAsync("fake", new ImportReadRequest(), Ct);

		ImportedKeyFile open = KeyOf(preview, "open");
		Assert.Equal(ImportedKeyStatus.Ready, open.Status);
		Assert.Equal(ReferenceKeys.Ed25519Fingerprint, open.Fingerprint);
		Assert.Equal("ssh-ed25519", open.Algorithm);
		Assert.False(open.NeedsPassphrase);
		Assert.Equal(open, KeyOf(preview, "again"));
		Assert.True(KeyOf(preview, "locked") is { Status: ImportedKeyStatus.Ready, NeedsPassphrase: true });
		Assert.Equal(ImportedKeyStatus.Missing, KeyOf(preview, "gone").Status);
		Assert.Contains("id_gone", KeyOf(preview, "gone").Reason, StringComparison.Ordinal);
		Assert.DoesNotContain("picked", KeyOf(preview, "gone").Reason, StringComparison.Ordinal);
		Assert.Equal(ImportedKeyStatus.Unreadable, KeyOf(preview, "text").Status);
		Assert.Equal(ImportedKeyStatus.Unsupported, KeyOf(preview, "public").Status);
		Assert.Equal(ImportedKeyStatus.Missing, KeyOf(preview, "token").Status);
		Assert.Contains("%C", KeyOf(preview, "token").Reason, StringComparison.Ordinal);
		Assert.Null(Assert.Single(preview.Entries, entry => entry.Name == "keyless").KeyFile);
	}

	[Fact]
	public async Task PreviewAsync_FromAPickedConfig_SaysWhereARelativeKeyWasLookedFor()
	{
		await using CoreTestContext context = Context();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		ConnectionImporter importer = Importer(scope, HostKind.Desktop, Entry("work", "id_work"));

		ImportPreview preview = await importer.PreviewAsync(
			"fake",
			new ImportReadRequest(new ImportFile { Name = "config", Content = [] }),
			Ct);

		ImportedKeyFile key = KeyOf(preview, "work");
		Assert.Equal(ImportedKeyStatus.Missing, key.Status);
		Assert.Contains("picked from a file", key.Reason, StringComparison.Ordinal);
		Assert.Contains("~/.ssh", key.Reason, StringComparison.Ordinal);
	}

	[Fact]
	public async Task PreviewAsync_NamesTheKeychainEntryThatAlreadyHoldsTheKey()
	{
		WriteKey("id_ed25519", ReferenceKeys.Ed25519);
		await using CoreTestContext context = Context();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		await SaveSharedKeyAsync(scope, "laptop key", ReferenceKeys.Ed25519);

		ImportPreview preview = await Importer(scope, HostKind.Desktop, Entry("web", "~/.ssh/id_ed25519")).PreviewAsync("fake", new ImportReadRequest(), Ct);

		Assert.True(KeyOf(preview, "web") is { Status: ImportedKeyStatus.InKeychain, KeychainName: "laptop key" });
	}

	[Fact]
	public async Task ImportAsync_AddsEachKeyOnce_AndSignsTheLoginsInWithIt()
	{
		TestKeyPair putty = TestKeyPair.Rsa();
		WriteKey("id_ed25519", ReferenceKeys.Ed25519);
		WriteKey("work.ppk", PuttyKeyWriter.Write(3, putty, "work", null));
		WriteKey("work-locked.ppk", PuttyKeyWriter.Write(2, putty, "work", Passphrase));
		WriteKey("other.ppk", PuttyKeyWriter.Write(3, TestKeyPair.Ed25519Key(), "other", Passphrase));
		await using CoreTestContext context = Context();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		ConnectionImporter importer = Importer(
			scope,
			HostKind.Desktop,
			Entry("web", "~/.ssh/id_ed25519"),
			Entry("web-again", "~/.ssh/id_ed25519") with { Address = "10.0.0.6" },
			Entry("work", "~/.ssh/work.ppk") with { Address = "10.0.1.1" },
			Entry("work-copy", "~/.ssh/work-locked.ppk") with { Address = "10.0.1.2" },
			Entry("other", "~/.ssh/other.ppk") with { Address = "10.0.2.1" });

		ImportResult result = await ImportPreviewedAsync(importer, importKeys: true);

		Assert.Null(result.Error);
		Assert.Equal(5, result.LoginsCreated);
		Assert.Equal(5, result.LoginsWithKeys);
		Assert.Empty(result.KeysNotAdded);

		// The OpenSSH key, the PuTTY key reached through two files with the same public key, and the locked one.
		Assert.Equal(3, result.KeysAdded);
		IReadOnlyList<CredentialInfo> keychain = await Store(scope).ListAsync(Ct);
		Assert.Equal(3, keychain.Count);
		Assert.All(keychain, credential => Assert.True(credential is { Kind: CredentialKind.PrivateKey, IsShared: true, HasPassphrase: false }));

		ConnectionCatalog catalog = await Repository(scope).GetCatalogAsync(Ct);
		Assert.All(catalog.Connections, login => Assert.Equal(AuthenticationMethod.PublicKey, login.AuthenticationMethod));
		Assert.Equal(LoginOn(catalog, "10.0.0.5").CredentialId, LoginOn(catalog, "10.0.0.6").CredentialId);
		Assert.Equal(LoginOn(catalog, "10.0.1.1").CredentialId, LoginOn(catalog, "10.0.1.2").CredentialId);
		Assert.All(catalog.Hosts, host => Assert.DoesNotContain("Key file", host.Notes ?? "", StringComparison.Ordinal));

		CredentialInfo work = Assert.Single(keychain, credential => credential.Id == LoginOn(catalog, "10.0.1.1").CredentialId);
		Assert.Equal("work", work.Name);
		Assert.Equal(putty.Fingerprint, work.KeyFingerprint);
		using (CredentialSecret secret = await Store(scope).RevealAsync(work.Id, Ct))
		{
			// The open PuTTY file came first, so the keychain holds it converted rather than the locked copy.
			Assert.StartsWith(OpenSshPrivateKeyFile.BeginMarker, secret.PrivateKey!.RevealString(), StringComparison.Ordinal);
		}

		CredentialInfo other = Assert.Single(keychain, credential => credential.Id == LoginOn(catalog, "10.0.2.1").CredentialId);
		Assert.Equal("ssh-ed25519", other.KeyAlgorithm);
		Assert.NotNull(other.KeyFingerprint);
		using (CredentialSecret secret = await Store(scope).RevealAsync(other.Id, Ct))
		{
			// Kept locked as PuTTY wrote it: the passphrase is asked when the login connects.
			Assert.StartsWith("PuTTY-User-Key-File-3", secret.PrivateKey!.RevealString(), StringComparison.Ordinal);
			Assert.Null(secret.Passphrase);
		}
	}

	[Fact]
	public async Task ImportAsync_UsesASharedKeyAlreadyInTheKeychain_ButNeverALoginsOwn()
	{
		WriteKey("id_ed25519", ReferenceKeys.Ed25519);
		await using CoreTestContext context = Context();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		CredentialInfo owned = await Store(scope).SaveAsync(
			new CredentialInfo { Id = Guid.NewGuid(), Name = "a login's own", Kind = CredentialKind.PrivateKey, OwnerConnectionId = Guid.NewGuid() },
			new CredentialSecretInput { PrivateKey = ReferenceKeys.Ed25519 },
			Ct);

		ImportResult first = await ImportPreviewedAsync(Importer(scope, HostKind.Desktop, Entry("web", "~/.ssh/id_ed25519")), importKeys: true);
		CredentialInfo added = Assert.Single(await Store(scope).ListAsync(Ct), credential => credential.Id != owned.Id);
		ImportResult second = await ImportPreviewedAsync(Importer(scope, HostKind.Desktop, Entry("web-root", "~/.ssh/id_ed25519") with { Username = "root" }), importKeys: true);

		Assert.Equal(1, first.KeysAdded);
		Assert.Equal(0, second.KeysAdded);
		Assert.Equal(1, second.LoginsWithKeys);
		Assert.Equal(2, (await Store(scope).ListAsync(Ct)).Count);
		ConnectionCatalog catalog = await Repository(scope).GetCatalogAsync(Ct);
		Assert.All(catalog.Connections, login => Assert.Equal(added.Id, login.CredentialId));
	}

	[Fact]
	public async Task ImportAsync_ImportsALoginWhoseKeyCannotCome_WithoutIt()
	{
		await using CoreTestContext context = Context();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		ConnectionImporter importer = Importer(scope, HostKind.Desktop, Entry("gone", "~/.ssh/id_gone") with { AuthenticationMethod = AuthenticationMethod.PublicKey });

		ImportResult result = await ImportPreviewedAsync(importer, importKeys: true);

		Assert.Equal(1, result.LoginsCreated);
		Assert.Equal(0, result.LoginsWithKeys);
		Assert.Contains("id_gone", Assert.Single(result.KeysNotAdded).Reason, StringComparison.Ordinal);
		ConnectionCatalog catalog = await Repository(scope).GetCatalogAsync(Ct);
		ConnectionProfile login = Assert.Single(catalog.Connections);
		Assert.Equal(AuthenticationMethod.Password, login.AuthenticationMethod);
		Assert.Null(login.CredentialId);
		Assert.Contains("~/.ssh/id_gone", Assert.Single(catalog.Hosts).Notes, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ImportAsync_WithoutTheTick_LeavesTheKeysWhereTheyAre()
	{
		WriteKey("id_ed25519", ReferenceKeys.Ed25519);
		await using CoreTestContext context = Context();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();

		ImportResult result = await ImportPreviewedAsync(Importer(scope, HostKind.Desktop, Entry("web", "~/.ssh/id_ed25519")), importKeys: false);

		Assert.Equal(0, result.KeysAdded);
		Assert.Empty(await Store(scope).ListAsync(Ct));
		ConnectionCatalog catalog = await Repository(scope).GetCatalogAsync(Ct);
		Assert.Null(Assert.Single(catalog.Connections).CredentialId);
		Assert.Contains("~/.ssh/id_ed25519", Assert.Single(catalog.Hosts).Notes, StringComparison.Ordinal);
	}

	[Fact]
	public async Task OnTheWebHost_NoKeyFileIsRead_EvenWhenTheRequestAsks()
	{
		WriteKey("id_ed25519", ReferenceKeys.Ed25519);
		await using CoreTestContext context = Context();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		ConnectionImporter importer = Importer(scope, HostKind.Web, Entry("web", "~/.ssh/id_ed25519"));

		Assert.False(importer.CanImportKeys);
		ImportPreview preview = await importer.PreviewAsync("fake", new ImportReadRequest(), Ct);
		Assert.Null(Assert.Single(preview.Entries).KeyFile);

		// A key state from anywhere else must not make the web host read the file either.
		ImportedEntry forged = Assert.Single(preview.Entries) with
		{
			KeyFile = new ImportedKeyFile { Status = ImportedKeyStatus.Ready, Path = Path.Combine(_home.Path, ".ssh", "id_ed25519") },
		};
		ImportResult result = await importer.ImportAsync(new ImportRequest { Entries = [forged], ImportKeys = true }, Ct);

		Assert.Equal(0, result.KeysAdded);
		Assert.Empty(await Store(scope).ListAsync(Ct));
		ConnectionCatalog catalog = await Repository(scope).GetCatalogAsync(Ct);
		Assert.Contains("~/.ssh/id_ed25519", Assert.Single(catalog.Hosts).Notes, StringComparison.Ordinal);
	}

	public ValueTask DisposeAsync() => _home.DisposeAsync();

	private static ImportedKeyFile KeyOf(ImportPreview preview, string name) =>
		Assert.Single(preview.Entries, entry => entry.Name == name).KeyFile ?? throw new InvalidOperationException($"{name} has no key state.");

	private static ConnectionProfile LoginOn(ConnectionCatalog catalog, string address) =>
		Assert.Single(catalog.Connections, login => catalog.FindHost(login.HostId)?.Address == address);

	private static ICredentialStore Store(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<ICredentialStore>();

	private static IConnectionRepository Repository(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<IConnectionRepository>();

	private static Task<CredentialInfo> SaveSharedKeyAsync(AsyncServiceScope scope, string name, string privateKey) =>
		Store(scope).SaveAsync(
			new CredentialInfo { Id = Guid.NewGuid(), Name = name, Kind = CredentialKind.PrivateKey, IsShared = true },
			new CredentialSecretInput { PrivateKey = privateKey },
			Ct).AsTask();

	private static async Task<ImportResult> ImportPreviewedAsync(ConnectionImporter importer, bool importKeys)
	{
		ImportPreview preview = await importer.PreviewAsync("fake", new ImportReadRequest(), Ct);
		return await importer.ImportAsync(new ImportRequest { Entries = preview.Entries, ImportKeys = importKeys }, Ct);
	}

	private static ImportedEntry Entry(string name, string? keyPath) => new()
	{
		Key = "fake:" + name,
		Name = name,
		Address = "10.0.0.5",
		ProtocolId = "ssh",
		Username = "deploy",
		IdentityFilePath = keyPath,
		IdentityPathStyle = IdentityPathStyle.OpenSsh,
	};

	private static CoreTestContext Context() =>
		new(services => services.AddSingleton<IPrivateKeyInspector, CoreKeyInspector>());

	private ConnectionImporter Importer(AsyncServiceScope scope, HostKind host, params ImportedEntry[] entries) =>
		new(
			[new FakeSource(entries)],
			Repository(scope),
			scope.ServiceProvider.GetRequiredService<ILogger<ConnectionImporter>>(),
			protocols: null,
			credentials: Store(scope),
			keyFiles: new IdentityFileReader(new FakeAppEnvironment(_home.Path) { Kind = host }, () => _home.Path, _ => null, "me", () => "desk"));

	private void WriteKey(string name, string text)
	{
		string folder = Path.Combine(_home.Path, ".ssh");
		Directory.CreateDirectory(folder);
		File.WriteAllText(Path.Combine(folder, name), text);
	}

	private sealed class FakeSource(ImportedEntry[] entries) : IConnectionImportSource
	{
		public ImportSourceInfo Info { get; } = new() { Id = "fake", DisplayName = "Fake" };

		public ValueTask<ImportPreview> ReadAsync(ImportReadRequest request, CancellationToken cancellationToken) =>
			ValueTask.FromResult(new ImportPreview { SourceId = "fake", Availability = ImportAvailability.Found, Entries = entries });
	}
}
