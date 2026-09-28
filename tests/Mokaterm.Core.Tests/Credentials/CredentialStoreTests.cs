using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Storage;
using Mokaterm.Core.Tests.TestSupport;

namespace Mokaterm.Core.Tests.Credentials;

public sealed class CredentialStoreTests
{
	private const string Password = "s3cret-password-7f3a";
	private const string ProxyPassword = "proxy-password-4c19";

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task SaveAsync_Password_ThenRevealReturnsSecret()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		ICredentialStore store = Store(scope);
		int changes = 0;
		store.Changed += () => changes++;

		CredentialInfo saved = await store.SaveAsync(PasswordInfo(), new CredentialSecretInput { Password = Password }, Ct);

		Assert.Equal("Production root", saved.Name);
		Assert.Equal("root", saved.Username);
		Assert.Equal(context.Time.GetUtcNow(), saved.CreatedAt);
		Assert.False(saved.HasPassphrase);
		Assert.False(saved.HasProxyPassword);
		Assert.Equal(1, changes);
		Assert.Equal(saved, await store.FindAsync(saved.Id, Ct));
		Assert.Equal(saved, Assert.Single(await store.ListAsync(Ct)));

		using CredentialSecret secret = await store.RevealAsync(saved.Id, Ct);
		Assert.Equal(CredentialKind.Password, secret.Kind);
		Assert.Equal(Password, secret.Password?.RevealString());
		Assert.Null(secret.PrivateKey);
		Assert.Null(secret.Passphrase);
		Assert.Null(secret.ProxyPassword);
	}

	[Fact]
	public async Task SaveAsync_ProxyPassword_IsRevealedAndFlagged()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		ICredentialStore store = Store(scope);

		CredentialInfo saved = await store.SaveAsync(
			PasswordInfo(),
			new CredentialSecretInput { Password = Password, ProxyPassword = ProxyPassword },
			Ct);

		Assert.True(saved.HasProxyPassword);
		using CredentialSecret secret = await store.RevealAsync(saved.Id, Ct);
		Assert.Equal(Password, secret.Password?.RevealString());
		Assert.Equal(ProxyPassword, secret.ProxyPassword?.RevealString());
	}

	[Fact]
	public async Task SaveAsync_ProxyPassword_NullKeepsEmptyClearsValueReplaces()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		ICredentialStore store = Store(scope);
		CredentialInfo created = await store.SaveAsync(
			PasswordInfo(),
			new CredentialSecretInput { Password = Password, ProxyPassword = ProxyPassword },
			Ct);

		CredentialInfo kept = await store.SaveAsync(created, new CredentialSecretInput { Password = "other-password" }, Ct);
		Assert.True(kept.HasProxyPassword);
		using (CredentialSecret secret = await store.RevealAsync(created.Id, Ct))
		{
			Assert.Equal("other-password", secret.Password?.RevealString());
			Assert.Equal(ProxyPassword, secret.ProxyPassword?.RevealString());
		}

		CredentialInfo replaced = await store.SaveAsync(kept, new CredentialSecretInput { ProxyPassword = "second-proxy-password" }, Ct);
		Assert.True(replaced.HasProxyPassword);
		using (CredentialSecret secret = await store.RevealAsync(created.Id, Ct))
		{
			Assert.Equal("other-password", secret.Password?.RevealString());
			Assert.Equal("second-proxy-password", secret.ProxyPassword?.RevealString());
		}

		CredentialInfo cleared = await store.SaveAsync(replaced, new CredentialSecretInput { ProxyPassword = "" }, Ct);
		Assert.False(cleared.HasProxyPassword);
		using CredentialSecret last = await store.RevealAsync(created.Id, Ct);
		Assert.Equal("other-password", last.Password?.RevealString());
		Assert.Null(last.ProxyPassword);
	}

	[Fact]
	public async Task SaveAsync_ProxyPasswordAlone_IsNotEnoughForANewPasswordCredential()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		ICredentialStore store = Store(scope);

		await Assert.ThrowsAsync<CredentialValidationException>(async () =>
			await store.SaveAsync(PasswordInfo(), new CredentialSecretInput { ProxyPassword = ProxyPassword }, Ct));

		Assert.Empty(await store.ListAsync(Ct));
	}

	[Fact]
	public async Task SaveAsync_EmptyId_AssignsNewId()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();

		CredentialInfo saved = await Store(scope).SaveAsync(PasswordInfo() with { Id = Guid.Empty }, new CredentialSecretInput { Password = Password }, Ct);

		Assert.NotEqual(Guid.Empty, saved.Id);
	}

	[Fact]
	public async Task SaveAsync_NewCredentialWithoutSecret_Throws()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		ICredentialStore store = Store(scope);

		await Assert.ThrowsAsync<ArgumentNullException>(async () => await store.SaveAsync(PasswordInfo(), null, Ct));
		await Assert.ThrowsAsync<CredentialValidationException>(async () => await store.SaveAsync(PasswordInfo(), new CredentialSecretInput { Password = "" }, Ct));
		await Assert.ThrowsAsync<ArgumentException>(async () => await store.SaveAsync(PasswordInfo() with { Name = "  " }, new CredentialSecretInput { Password = Password }, Ct));
		Assert.Empty(await store.ListAsync(Ct));
	}

	[Fact]
	public async Task SaveAsync_Update_NullKeepsEmptyClearsValueReplaces()
	{
		await using CoreTestContext context = new(services => services.AddSingleton<IPrivateKeyInspector>(new FakePrivateKeyInspector()));
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		ICredentialStore store = Store(scope);
		CredentialInfo created = await store.SaveAsync(
			KeyInfo(),
			new CredentialSecretInput { PrivateKey = FakePrivateKeyInspector.EncryptedKey, Passphrase = FakePrivateKeyInspector.Passphrase, Password = Password },
			Ct);
		Assert.True(created.HasPassphrase);

		context.Time.Advance(TimeSpan.FromMinutes(1));
		CredentialInfo renamed = await store.SaveAsync(created with { Name = "Renamed" }, secret: null, Ct);
		Assert.Equal("Renamed", renamed.Name);
		Assert.Equal(created.CreatedAt, renamed.CreatedAt);
		Assert.Equal(context.Time.GetUtcNow(), renamed.UpdatedAt);
		using (CredentialSecret kept = await store.RevealAsync(created.Id, Ct))
		{
			Assert.Equal(FakePrivateKeyInspector.EncryptedKey, kept.PrivateKey?.RevealString());
			Assert.Equal(FakePrivateKeyInspector.Passphrase, kept.Passphrase?.RevealString());
			Assert.Equal(Password, kept.Password?.RevealString());
		}

		CredentialInfo replaced = await store.SaveAsync(renamed, new CredentialSecretInput { PrivateKey = FakePrivateKeyInspector.PlainKey, Passphrase = "", Password = "" }, Ct);
		Assert.False(replaced.HasPassphrase);
		Assert.Equal(FakePrivateKeyInspector.FingerprintOf(FakePrivateKeyInspector.PlainKey), replaced.KeyFingerprint);
		using CredentialSecret updated = await store.RevealAsync(created.Id, Ct);
		Assert.Equal(FakePrivateKeyInspector.PlainKey, updated.PrivateKey?.RevealString());
		Assert.Null(updated.Passphrase);
		Assert.Null(updated.Password);
	}

	[Fact]
	public async Task SaveAsync_PrivateKey_InspectorFillsAlgorithmAndFingerprint()
	{
		FakePrivateKeyInspector inspector = new();
		await using CoreTestContext context = new(services => services.AddSingleton<IPrivateKeyInspector>(inspector));
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		ICredentialStore store = Store(scope);

		CredentialInfo saved = await store.SaveAsync(
			KeyInfo() with { KeyAlgorithm = "caller value", KeyFingerprint = "caller value", HasPassphrase = true },
			new CredentialSecretInput { PrivateKey = FakePrivateKeyInspector.PlainKey },
			Ct);

		Assert.Equal(FakePrivateKeyInspector.Algorithm, saved.KeyAlgorithm);
		Assert.Equal(FakePrivateKeyInspector.FingerprintOf(FakePrivateKeyInspector.PlainKey), saved.KeyFingerprint);
		Assert.False(saved.HasPassphrase);
		Assert.Equal(1, inspector.Calls);

		CredentialInfo renamed = await store.SaveAsync(saved with { Name = "Other name" }, null, Ct);
		Assert.Equal(saved.KeyFingerprint, renamed.KeyFingerprint);
		Assert.Equal(1, inspector.Calls);
	}

	[Theory]
	[InlineData(null, PrivateKeyStatus.PassphraseRequired)]
	[InlineData("wrong", PrivateKeyStatus.WrongPassphrase)]
	public async Task SaveAsync_PrivateKeyTheInspectorRejects_ThrowsAndSavesNothing(string? passphrase, PrivateKeyStatus expected)
	{
		await using CoreTestContext context = new(services => services.AddSingleton<IPrivateKeyInspector>(new FakePrivateKeyInspector()));
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		ICredentialStore store = Store(scope);

		CredentialValidationException error = await Assert.ThrowsAsync<CredentialValidationException>(async () =>
			await store.SaveAsync(KeyInfo(), new CredentialSecretInput { PrivateKey = FakePrivateKeyInspector.EncryptedKey, Passphrase = passphrase }, Ct));

		Assert.Equal(expected, error.Status);
		Assert.False(string.IsNullOrWhiteSpace(error.Message));
		Assert.Empty(await store.ListAsync(Ct));
	}

	[Fact]
	public async Task SaveAsync_ChangingOnlyThePassphrase_ReinspectsTheStoredKey()
	{
		await using CoreTestContext context = new(services => services.AddSingleton<IPrivateKeyInspector>(new FakePrivateKeyInspector()));
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		ICredentialStore store = Store(scope);
		CredentialInfo saved = await store.SaveAsync(
			KeyInfo(),
			new CredentialSecretInput { PrivateKey = FakePrivateKeyInspector.EncryptedKey, Passphrase = FakePrivateKeyInspector.Passphrase },
			Ct);

		CredentialValidationException error = await Assert.ThrowsAsync<CredentialValidationException>(async () =>
			await store.SaveAsync(saved, new CredentialSecretInput { Passphrase = "" }, Ct));

		Assert.Equal(PrivateKeyStatus.PassphraseRequired, error.Status);
		Assert.True((await store.FindAsync(saved.Id, Ct))?.HasPassphrase);
	}

	[Fact]
	public async Task SaveAsync_PrivateKeyWithoutInspector_SavesWithoutKeyDetails()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();

		CredentialInfo saved = await Store(scope).SaveAsync(KeyInfo(), new CredentialSecretInput { PrivateKey = "not even a key" }, Ct);

		Assert.Null(saved.KeyAlgorithm);
		Assert.Null(saved.KeyFingerprint);
		using CredentialSecret secret = await Store(scope).RevealAsync(saved.Id, Ct);
		Assert.Equal("not even a key", secret.PrivateKey?.RevealString());
	}

	[Fact]
	public async Task RevealAsync_UnknownId_ThrowsKeyNotFound()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();

		await Assert.ThrowsAsync<KeyNotFoundException>(async () => await Store(scope).RevealAsync(Guid.NewGuid(), Ct));
	}

	[Fact]
	public async Task CredentialsDocument_HoldsNoDecryptedSecrets()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		await Store(scope).SaveAsync(PasswordInfo(), new CredentialSecretInput { Password = Password }, Ct);

		JsonObject? document = await scope.ServiceProvider.GetRequiredService<IVaultDataStore>().ReadAsync<JsonObject>("credentials", Ct);

		string json = document?.ToJsonString() ?? "";
		Assert.Contains("Production root", json, StringComparison.Ordinal);
		Assert.DoesNotContain(Password, json, StringComparison.Ordinal);
	}

	[Fact]
	public async Task RevealAsync_SealedFieldMovedToAnotherCredential_FailsToDecrypt()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		ICredentialStore store = Store(scope);
		CredentialInfo first = await store.SaveAsync(PasswordInfo(), new CredentialSecretInput { Password = Password }, Ct);
		CredentialInfo second = await store.SaveAsync(PasswordInfo() with { Id = Guid.NewGuid(), Name = "Second" }, new CredentialSecretInput { Password = "other" }, Ct);
		IVaultDataStore vaultData = scope.ServiceProvider.GetRequiredService<IVaultDataStore>();
		JsonObject document = (await vaultData.ReadAsync<JsonObject>("credentials", Ct))!;
		JsonArray credentials = document["credentials"]!.AsArray();
		credentials[1]!["password"] = credentials[0]!["password"]!.DeepClone();
		await vaultData.WriteAsync("credentials", document, Ct);

		await using AsyncServiceScope fresh = await context.UnlockedScopeAsync();

		using (CredentialSecret untouched = await Store(fresh).RevealAsync(first.Id, Ct))
		{
			Assert.Equal(Password, untouched.Password?.RevealString());
		}

		await Assert.ThrowsAsync<AuthenticationTagMismatchException>(async () => await Store(fresh).RevealAsync(second.Id, Ct));
	}

	[Fact]
	public async Task DeleteAsync_RemovesCredential()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		ICredentialStore store = Store(scope);
		CredentialInfo saved = await store.SaveAsync(PasswordInfo(), new CredentialSecretInput { Password = Password }, Ct);
		int changes = 0;
		store.Changed += () => changes++;

		await store.DeleteAsync(saved.Id, Ct);
		await store.DeleteAsync(saved.Id, Ct);

		Assert.Null(await store.FindAsync(saved.Id, Ct));
		Assert.Equal(1, changes);
	}

	[Fact]
	public async Task Store_WhileLocked_Throws()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		ICredentialStore store = Store(scope);
		CredentialInfo saved = await store.SaveAsync(PasswordInfo(), new CredentialSecretInput { Password = Password }, Ct);
		scope.ServiceProvider.GetRequiredService<IVault>().Lock();

		await Assert.ThrowsAsync<VaultLockedException>(async () => await store.ListAsync(Ct));
		await Assert.ThrowsAsync<VaultLockedException>(async () => await store.RevealAsync(saved.Id, Ct));
		await Assert.ThrowsAsync<VaultLockedException>(async () => await store.SaveAsync(PasswordInfo(), new CredentialSecretInput { Password = Password }, Ct));
	}

	private static ICredentialStore Store(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<ICredentialStore>();

	private static CredentialInfo PasswordInfo() => new()
	{
		Id = Guid.NewGuid(),
		Name = "  Production root ",
		Kind = CredentialKind.Password,
		Username = " root ",
		IsShared = true,
	};

	private static CredentialInfo KeyInfo() => new()
	{
		Id = Guid.NewGuid(),
		Name = "Deploy key",
		Kind = CredentialKind.PrivateKey,
		Username = "deploy",
		IsShared = true,
	};
}
