using Microsoft.Extensions.DependencyInjection;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Storage;
using Mokaterm.Core.Tests.TestSupport;

namespace Mokaterm.Core.Tests.Security;

public sealed class KnownHostsStoreTests
{
	private const string Ed25519Fingerprint = "SHA256:uNiVztksCsDhcc0u9e8BujQXVUpKZIDTMczCvj3tD2s";

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task MatchAsync_NothingStored_IsUnknown()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		IKnownHostsStore store = scope.ServiceProvider.GetRequiredService<IKnownHostsStore>();

		Assert.Equal(HostIdentityMatch.Unknown, await store.MatchAsync(SshIdentity(), Ct));
		Assert.Empty(await store.ListAsync(Ct));
	}

	[Fact]
	public async Task TrustAsync_ThenMatch_IsTrustedAndStoredNormalized()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		IKnownHostsStore store = scope.ServiceProvider.GetRequiredService<IKnownHostsStore>();
		int changes = 0;
		store.Changed += () => changes++;

		await store.TrustAsync(SshIdentity(host: "  Build-01.Example.COM "), Ct);

		KnownHost entry = Assert.Single(await store.ListAsync(Ct));
		Assert.Equal("build-01.example.com", entry.Host);
		Assert.Equal(context.Time.GetUtcNow(), entry.AddedAt);
		Assert.Equal(1, changes);
		Assert.Equal(HostIdentityMatch.Trusted, await store.MatchAsync(SshIdentity(host: "build-01.example.com"), Ct));
	}

	[Theory]
	[InlineData("[::1]", "::1")]
	[InlineData("::1", "[::1]")]
	[InlineData("[FE80::1]", "fe80::1")]
	public async Task MatchAsync_Ipv6WithOrWithoutBrackets_IsSameHost(string trusted, string presented)
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		IKnownHostsStore store = scope.ServiceProvider.GetRequiredService<IKnownHostsStore>();

		await store.TrustAsync(SshIdentity(host: trusted), Ct);

		Assert.Equal(HostIdentityMatch.Trusted, await store.MatchAsync(SshIdentity(host: presented), Ct));
	}

	[Fact]
	public async Task MatchAsync_DifferentFingerprint_IsChanged()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		IKnownHostsStore store = scope.ServiceProvider.GetRequiredService<IKnownHostsStore>();
		await store.TrustAsync(SshIdentity(), Ct);

		Assert.Equal(HostIdentityMatch.Changed, await store.MatchAsync(SshIdentity(fingerprint: "SHA256:somethingElseEntirely0000000000000000000000"), Ct));
	}

	[Fact]
	public async Task MatchAsync_OtherPortKindOrAlgorithm_IsUnknown()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		IKnownHostsStore store = scope.ServiceProvider.GetRequiredService<IKnownHostsStore>();
		await store.TrustAsync(SshIdentity(), Ct);

		Assert.Equal(HostIdentityMatch.Unknown, await store.MatchAsync(SshIdentity(port: 2222), Ct));
		Assert.Equal(HostIdentityMatch.Unknown, await store.MatchAsync(SshIdentity(algorithm: "ecdsa-sha2-nistp256", fingerprint: "SHA256:other"), Ct));
		Assert.Equal(HostIdentityMatch.Unknown, await store.MatchAsync(SshIdentity() with { Kind = HostIdentityKind.TlsCertificate }, Ct));
	}

	[Fact]
	public async Task TrustAsync_SameSlot_ReplacesEntry()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		IKnownHostsStore store = scope.ServiceProvider.GetRequiredService<IKnownHostsStore>();
		await store.TrustAsync(SshIdentity(), Ct);
		await store.TrustAsync(SshIdentity(algorithm: "rsa-sha2-512", fingerprint: "SHA256:rsa"), Ct);

		await store.TrustAsync(SshIdentity(fingerprint: "SHA256:reinstalled"), Ct);

		IReadOnlyList<KnownHost> hosts = await store.ListAsync(Ct);
		Assert.Equal(2, hosts.Count);
		Assert.Contains(hosts, host => host.Algorithm == "ssh-ed25519" && host.Fingerprint == "SHA256:reinstalled");
		Assert.Equal(HostIdentityMatch.Trusted, await store.MatchAsync(SshIdentity(fingerprint: "SHA256:reinstalled"), Ct));
	}

	[Fact]
	public async Task MatchAsync_UpdatesLastSeenAtMostOncePerDay()
	{
		// Auto-lock would end the session while the clock jumps a day ahead.
		await using CoreTestContext context = new(services => services.AddSingleton<IAppDataStore>(new InMemoryAppDataStore()));
		await context.Services.GetRequiredService<ISettingsService>().UpdateAsync<SecuritySettings>(settings => settings with { AutoLockMinutes = 0 }, Ct);
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		IKnownHostsStore store = scope.ServiceProvider.GetRequiredService<IKnownHostsStore>();
		await store.TrustAsync(SshIdentity(), Ct);
		DateTimeOffset trustedAt = context.Time.GetUtcNow();
		int changes = 0;
		store.Changed += () => changes++;

		context.Time.Advance(TimeSpan.FromHours(23));
		await store.MatchAsync(SshIdentity(), Ct);
		Assert.Equal(trustedAt, Assert.Single(await store.ListAsync(Ct)).LastSeenAt);
		Assert.Equal(0, changes);

		context.Time.Advance(TimeSpan.FromHours(1));
		await store.MatchAsync(SshIdentity(), Ct);
		Assert.Equal(context.Time.GetUtcNow(), Assert.Single(await store.ListAsync(Ct)).LastSeenAt);
		Assert.Equal(1, changes);
	}

	[Fact]
	public async Task RemoveAsync_DeletesEntry()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		IKnownHostsStore store = scope.ServiceProvider.GetRequiredService<IKnownHostsStore>();
		await store.TrustAsync(SshIdentity(), Ct);

		await store.RemoveAsync(Assert.Single(await store.ListAsync(Ct)), Ct);

		Assert.Empty(await store.ListAsync(Ct));
		Assert.Equal(HostIdentityMatch.Unknown, await store.MatchAsync(SshIdentity(), Ct));
	}

	[Fact]
	public async Task MatchAsync_CertificateFingerprintCaseAndColons_AreIgnored()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		IKnownHostsStore store = scope.ServiceProvider.GetRequiredService<IKnownHostsStore>();
		HostIdentity certificate = new()
		{
			Host = "ftp.example.com",
			Port = 21,
			Kind = HostIdentityKind.TlsCertificate,
			Algorithm = "RSA 2048",
			Fingerprint = "AB:CD:EF:01",
		};

		await store.TrustAsync(certificate, Ct);

		Assert.Equal("ABCDEF01", Assert.Single(await store.ListAsync(Ct)).Fingerprint);
		Assert.Equal(HostIdentityMatch.Trusted, await store.MatchAsync(certificate with { Fingerprint = "abcdef01" }, Ct));
	}

	[Fact]
	public async Task Store_WhileLocked_Throws()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		IKnownHostsStore store = scope.ServiceProvider.GetRequiredService<IKnownHostsStore>();
		scope.ServiceProvider.GetRequiredService<IVault>().Lock();

		await Assert.ThrowsAsync<VaultLockedException>(async () => await store.MatchAsync(SshIdentity(), Ct));
		await Assert.ThrowsAsync<VaultLockedException>(() => store.TrustAsync(SshIdentity(), Ct));
	}

	internal static HostIdentity SshIdentity(string host = "10.10.2.3", int port = 22, string algorithm = "ssh-ed25519", string fingerprint = Ed25519Fingerprint) => new()
	{
		Host = host,
		Port = port,
		Kind = HostIdentityKind.SshHostKey,
		Algorithm = algorithm,
		Fingerprint = fingerprint,
	};
}
