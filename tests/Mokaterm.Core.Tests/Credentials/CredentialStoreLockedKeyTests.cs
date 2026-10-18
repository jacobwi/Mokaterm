using Microsoft.Extensions.DependencyInjection;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Core.Keys;
using Mokaterm.Core.Tests.Keys;
using Mokaterm.Core.Tests.TestSupport;

namespace Mokaterm.Core.Tests.Credentials;

/// <summary>A passphrase protected key saved without its passphrase, which only the import asks for.</summary>
public sealed class CredentialStoreLockedKeyTests
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task SaveAsync_KeepsALockedKeyOnRequest_NamedByItsPublicHalf()
	{
		await using CoreTestContext context = new(services => services.AddSingleton<IPrivateKeyInspector, CoreKeyInspector>());
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		ICredentialStore store = scope.ServiceProvider.GetRequiredService<ICredentialStore>();

		CredentialInfo saved = await store.SaveAsync(
			Key("locked"),
			new CredentialSecretInput { PrivateKey = ReferenceKeys.EncryptedEd25519, AskPassphraseWhenUsed = true },
			Ct);

		Assert.False(saved.HasPassphrase);
		Assert.Equal("ssh-ed25519", saved.KeyAlgorithm);
		Assert.Equal(new SshKeyGenerator().ReadPublicKey(ReferenceKeys.EncryptedEd25519)!.Fingerprint, saved.KeyFingerprint);
		using CredentialSecret secret = await store.RevealAsync(saved.Id, Ct);
		Assert.Equal(ReferenceKeys.EncryptedEd25519, secret.PrivateKey!.RevealString());
		Assert.Null(secret.Passphrase);
	}

	[Fact]
	public async Task SaveAsync_StillAsksForThePassphrase_WhenNobodySaidOtherwise()
	{
		await using CoreTestContext context = new(services => services.AddSingleton<IPrivateKeyInspector, CoreKeyInspector>());
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		ICredentialStore store = scope.ServiceProvider.GetRequiredService<ICredentialStore>();

		CredentialValidationException failure = await Assert.ThrowsAsync<CredentialValidationException>(
			() => store.SaveAsync(Key("locked"), new CredentialSecretInput { PrivateKey = ReferenceKeys.EncryptedEd25519 }, Ct).AsTask());

		Assert.Equal(PrivateKeyStatus.PassphraseRequired, failure.Status);
	}

	[Fact]
	public void SecretInput_PrintsTheFlagButNeverTheKey()
	{
		string printed = new CredentialSecretInput { PrivateKey = ReferenceKeys.Ed25519, AskPassphraseWhenUsed = true }.ToString();

		Assert.Contains("AskPassphraseWhenUsed = True", printed, StringComparison.Ordinal);
		Assert.DoesNotContain("OPENSSH", printed, StringComparison.Ordinal);
	}

	private static CredentialInfo Key(string name) => new() { Id = Guid.NewGuid(), Name = name, Kind = CredentialKind.PrivateKey, IsShared = true };
}
