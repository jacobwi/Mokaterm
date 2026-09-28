using Microsoft.Extensions.DependencyInjection;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Core.Keys;
using Mokaterm.Core.Tests.TestSupport;

namespace Mokaterm.Core.Tests.Keys;

public sealed class GeneratedKeyStorageTests
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task GeneratedKey_SurvivesTheVaultUnchangedAndStillShowsTheSamePublicKey()
	{
		SshKeyGenerator generator = new();
		GeneratedSshKey key = await generator.GenerateAsync(
			new SshKeyRequest { Type = SshKeyType.Ed25519, Comment = "me@box", Passphrase = "hunter2" },
			Ct);

		await using CoreTestContext context = new();
		Guid id = Guid.NewGuid();
		await using (AsyncServiceScope scope = await context.CreateVaultScopeAsync())
		{
			ICredentialStore store = scope.ServiceProvider.GetRequiredService<ICredentialStore>();
			await store.SaveAsync(
				new CredentialInfo { Id = id, Name = "Deploy key", Kind = CredentialKind.PrivateKey, IsShared = true },
				new CredentialSecretInput { PrivateKey = key.PrivateKey, Passphrase = "hunter2" },
				Ct);
		}

		// A new scope means the document is read back from disk and decrypted again.
		await using AsyncServiceScope reopened = await context.UnlockedScopeAsync();
		ICredentialStore reopenedStore = reopened.ServiceProvider.GetRequiredService<ICredentialStore>();
		using CredentialSecret secret = await reopenedStore.RevealAsync(id, Ct);

		Assert.NotNull(secret.PrivateKey);
		string stored = secret.PrivateKey.RevealString();
		Assert.Equal(key.PrivateKey, stored);
		Assert.Equal("hunter2", secret.Passphrase?.RevealString());

		SshPublicKey? readBack = generator.ReadPublicKey(stored);
		Assert.NotNull(readBack);
		Assert.Equal(key.PublicKey.AuthorizedKeysLine, readBack.AuthorizedKeysLine + " me@box");
		Assert.Equal(key.PublicKey.Fingerprint, readBack.Fingerprint);
	}
}
