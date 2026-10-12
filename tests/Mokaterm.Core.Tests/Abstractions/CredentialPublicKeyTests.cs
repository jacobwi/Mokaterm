using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Security;

namespace Mokaterm.Core.Tests.Abstractions;

/// <summary>Reading a saved key's public half: the secret is wiped again, and a locked vault or a missing key says so.</summary>
public sealed class CredentialPublicKeyTests
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task ReadsThePublicHalf_AndWipesTheSecret()
	{
		Store store = new(new CredentialSecret(CredentialKind.PrivateKey, null, SecretBuffer.FromString("-----BEGIN OPENSSH PRIVATE KEY-----"), null));

		(bool hasKey, SshPublicKey? key) = await CredentialPublicKey.ReadAsync(store, new Keys(), Guid.NewGuid(), Ct);

		Assert.True(hasKey);
		Assert.Equal("ssh-ed25519 AAAA test", key?.AuthorizedKeysLine);
		Assert.True(store.Revealed!.PrivateKey!.IsDisposed);
	}

	[Fact]
	public async Task APassword_HasNoKey_AndAKeyInAnotherFormat_HasNoPublicHalf()
	{
		Store password = new(new CredentialSecret(CredentialKind.Password, SecretBuffer.FromString("hunter2"), null, null));
		Store putty = new(new CredentialSecret(CredentialKind.PrivateKey, null, SecretBuffer.FromString("PuTTY-User-Key-File-3"), null));

		Assert.False((await CredentialPublicKey.ReadAsync(password, new Keys(), Guid.NewGuid(), Ct)).HasKey);
		(bool hasKey, SshPublicKey? key) = await CredentialPublicKey.ReadAsync(putty, new Keys(), Guid.NewGuid(), Ct);
		Assert.True(hasKey);
		Assert.Null(key);
	}

	[Fact]
	public async Task ALockedVault_Throws() =>
		await Assert.ThrowsAsync<VaultLockedException>(() => CredentialPublicKey.ReadAsync(new Store(null), new Keys(), Guid.NewGuid(), Ct));

	private sealed class Store(CredentialSecret? secret) : ICredentialStore
	{
		public event Action? Changed
		{
			add { }
			remove { }
		}

		public CredentialSecret? Revealed { get; private set; }

		public ValueTask<CredentialSecret> RevealAsync(Guid id, CancellationToken cancellationToken = default)
		{
			Revealed = secret ?? throw new VaultLockedException();
			return ValueTask.FromResult(secret);
		}

		public ValueTask<IReadOnlyList<CredentialInfo>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public ValueTask<CredentialInfo?> FindAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public ValueTask<CredentialInfo> SaveAsync(CredentialInfo info, CredentialSecretInput? secret, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
	}

	private sealed class Keys : ISshKeyGenerator
	{
		public Task<GeneratedSshKey> GenerateAsync(SshKeyRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public SshPublicKey? ReadPublicKey(string privateKey) =>
			privateKey.StartsWith("-----BEGIN OPENSSH", StringComparison.Ordinal) ? new SshPublicKey("ssh-ed25519", "test", "ssh-ed25519 AAAA test", "SHA256:x") : null;
	}
}
