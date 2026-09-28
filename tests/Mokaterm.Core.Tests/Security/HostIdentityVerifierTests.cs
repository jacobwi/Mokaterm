using Microsoft.Extensions.DependencyInjection;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Storage;
using Mokaterm.Core.Tests.TestSupport;

namespace Mokaterm.Core.Tests.Security;

public sealed class HostIdentityVerifierTests
{
	private static readonly HostIdentity Identity = KnownHostsStoreTests.SshIdentity();
	private static readonly HostIdentity ChangedIdentity = KnownHostsStoreTests.SshIdentity(fingerprint: "SHA256:reinstalledServerKey");

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Theory]
	[InlineData(HostKeyPolicy.Ask)]
	[InlineData(HostKeyPolicy.AcceptNew)]
	[InlineData(HostKeyPolicy.Strict)]
	public async Task VerifyAsync_Trusted_AcceptsWithoutAsking(HostKeyPolicy policy)
	{
		await using CoreTestContext context = CreateContext();
		await using AsyncServiceScope scope = await PrepareAsync(context, policy, trusted: Identity);

		Assert.True(await Verifier(scope).VerifyAsync(Identity, Ct));
		Assert.Empty(context.Interaction.HostTrustPrompts);
		Assert.Empty(context.Interaction.Notices);
	}

	[Fact]
	public async Task VerifyAsync_UnknownStrict_RefusesWithError()
	{
		await using CoreTestContext context = CreateContext();
		await using AsyncServiceScope scope = await PrepareAsync(context, HostKeyPolicy.Strict);

		Assert.False(await Verifier(scope).VerifyAsync(Identity, Ct));

		Notice notice = Assert.Single(context.Interaction.Notices);
		Assert.Equal(NoticeSeverity.Error, notice.Severity);
		Assert.Contains("10.10.2.3:22", notice.Message, StringComparison.Ordinal);
		Assert.Empty(context.Interaction.HostTrustPrompts);
		Assert.Empty(await KnownHosts(scope).ListAsync(Ct));
	}

	[Fact]
	public async Task VerifyAsync_UnknownAcceptNew_TrustsWithInfoNotice()
	{
		await using CoreTestContext context = CreateContext();
		await using AsyncServiceScope scope = await PrepareAsync(context, HostKeyPolicy.AcceptNew);

		Assert.True(await Verifier(scope).VerifyAsync(Identity, Ct));

		Assert.Equal(NoticeSeverity.Info, Assert.Single(context.Interaction.Notices).Severity);
		Assert.Empty(context.Interaction.HostTrustPrompts);
		Assert.Equal(HostIdentityMatch.Trusted, await KnownHosts(scope).MatchAsync(Identity, Ct));
	}

	[Fact]
	public async Task VerifyAsync_UnknownAskRejected_Refuses()
	{
		await using CoreTestContext context = CreateContext();
		await using AsyncServiceScope scope = await PrepareAsync(context, HostKeyPolicy.Ask);
		context.Interaction.HostTrustDecision = HostTrustDecision.Reject;

		Assert.False(await Verifier(scope).VerifyAsync(Identity, Ct));

		HostTrustPrompt prompt = Assert.Single(context.Interaction.HostTrustPrompts);
		Assert.Equal(HostIdentityMatch.Unknown, prompt.Match);
		Assert.Null(prompt.Previous);
		Assert.Same(Identity, prompt.Identity);
		Assert.Empty(await KnownHosts(scope).ListAsync(Ct));
	}

	[Fact]
	public async Task VerifyAsync_UnknownAskAcceptOnce_RemembersForThisScopeOnly()
	{
		await using CoreTestContext context = CreateContext();
		await using AsyncServiceScope scope = await PrepareAsync(context, HostKeyPolicy.Ask);
		context.Interaction.HostTrustDecision = HostTrustDecision.AcceptOnce;

		Assert.True(await Verifier(scope).VerifyAsync(Identity, Ct));
		Assert.True(await Verifier(scope).VerifyAsync(Identity, Ct));

		Assert.Single(context.Interaction.HostTrustPrompts);
		Assert.Empty(await KnownHosts(scope).ListAsync(Ct));

		context.Interaction.HostTrustDecision = HostTrustDecision.Reject;
		Assert.False(await Verifier(scope).VerifyAsync(ChangedIdentity, Ct));

		await using AsyncServiceScope otherScope = await context.UnlockedScopeAsync();
		Assert.False(await Verifier(otherScope).VerifyAsync(Identity, Ct));
		Assert.Equal(3, context.Interaction.HostTrustPrompts.Count);
	}

	[Fact]
	public async Task VerifyAsync_UnknownAskAcceptAndSave_StoresIdentity()
	{
		await using CoreTestContext context = CreateContext();
		await using AsyncServiceScope scope = await PrepareAsync(context, HostKeyPolicy.Ask);
		context.Interaction.HostTrustDecision = HostTrustDecision.AcceptAndSave;

		Assert.True(await Verifier(scope).VerifyAsync(Identity, Ct));

		Assert.Equal(HostIdentityMatch.Trusted, await KnownHosts(scope).MatchAsync(Identity, Ct));
	}

	[Theory]
	[InlineData(HostKeyPolicy.Strict)]
	[InlineData(HostKeyPolicy.AcceptNew)]
	public async Task VerifyAsync_ChangedWithoutAsking_RefusesWithWarningNamingHost(HostKeyPolicy policy)
	{
		await using CoreTestContext context = CreateContext();
		await using AsyncServiceScope scope = await PrepareAsync(context, policy, trusted: Identity);

		Assert.False(await Verifier(scope).VerifyAsync(ChangedIdentity, Ct));

		Notice notice = Assert.Single(context.Interaction.Notices);
		Assert.Equal(NoticeSeverity.Warning, notice.Severity);
		Assert.Contains("10.10.2.3", notice.Message, StringComparison.Ordinal);
		Assert.Empty(context.Interaction.HostTrustPrompts);
		Assert.Equal(HostIdentityMatch.Changed, await KnownHosts(scope).MatchAsync(ChangedIdentity, Ct));
	}

	[Fact]
	public async Task VerifyAsync_ChangedAsk_PromptsWithPreviousAndSavesReplacement()
	{
		await using CoreTestContext context = CreateContext();
		await using AsyncServiceScope scope = await PrepareAsync(context, HostKeyPolicy.Ask, trusted: Identity);
		context.Interaction.HostTrustDecision = HostTrustDecision.AcceptAndSave;

		Assert.True(await Verifier(scope).VerifyAsync(ChangedIdentity, Ct));

		HostTrustPrompt prompt = Assert.Single(context.Interaction.HostTrustPrompts);
		Assert.Equal(HostIdentityMatch.Changed, prompt.Match);
		Assert.Equal(Identity.Fingerprint, prompt.Previous?.Fingerprint);
		Assert.Equal(HostIdentityMatch.Trusted, await KnownHosts(scope).MatchAsync(ChangedIdentity, Ct));
		Assert.Equal(HostIdentityMatch.Changed, await KnownHosts(scope).MatchAsync(Identity, Ct));
	}

	[Theory]
	[InlineData(HostKeyPolicy.Ask)]
	[InlineData(HostKeyPolicy.Strict)]
	public async Task VerifyAsync_UnknownCertificateTrustedByOs_AcceptsWithoutAsking(HostKeyPolicy policy)
	{
		await using CoreTestContext context = CreateContext();
		await using AsyncServiceScope scope = await PrepareAsync(context, policy);
		HostIdentity certificate = Certificate(chainTrusted: true);

		Assert.True(await Verifier(scope).VerifyAsync(certificate, Ct));

		Assert.Empty(context.Interaction.HostTrustPrompts);
		Assert.Empty(context.Interaction.Notices);
		Assert.Empty(await KnownHosts(scope).ListAsync(Ct));
	}

	[Fact]
	public async Task VerifyAsync_UntrustedCertificate_FollowsPolicy()
	{
		await using CoreTestContext context = CreateContext();
		await using AsyncServiceScope scope = await PrepareAsync(context, HostKeyPolicy.Ask);
		context.Interaction.HostTrustDecision = HostTrustDecision.Reject;

		Assert.False(await Verifier(scope).VerifyAsync(Certificate(chainTrusted: false), Ct));

		Assert.Single(context.Interaction.HostTrustPrompts);
	}

	[Fact]
	public async Task VerifyAsync_ChangedCertificateEvenIfOsTrustsIt_Asks()
	{
		await using CoreTestContext context = CreateContext();
		await using AsyncServiceScope scope = await PrepareAsync(context, HostKeyPolicy.Ask, trusted: Certificate(chainTrusted: true));
		context.Interaction.HostTrustDecision = HostTrustDecision.Reject;

		Assert.False(await Verifier(scope).VerifyAsync(Certificate(chainTrusted: true) with { Fingerprint = "00112233" }, Ct));

		Assert.Equal(HostIdentityMatch.Changed, Assert.Single(context.Interaction.HostTrustPrompts).Match);
	}

	[Fact]
	public async Task VerifyAsync_SameUnknownHostTwiceAtOnce_AsksOnceAndSharesTheAnswer()
	{
		await using CoreTestContext context = CreateContext();
		await using AsyncServiceScope scope = await PrepareAsync(context, HostKeyPolicy.Ask);
		TaskCompletionSource userAnswers = new(TaskCreationOptions.RunContinuationsAsynchronously);
		context.Interaction.HostTrustDecision = HostTrustDecision.AcceptAndSave;
		context.Interaction.HostTrustGate = userAnswers.Task;

		Task<bool> first = Verifier(scope).VerifyAsync(Identity, Ct).AsTask();
		await context.Interaction.HostTrustPromptShown.WaitAsync(Ct);
		Task<bool> second = Verifier(scope).VerifyAsync(Identity, Ct).AsTask();

		// Time for the second session to look the host up and reach the prompt it must not open.
		await Task.Delay(TimeSpan.FromMilliseconds(250), Ct);
		userAnswers.SetResult();

		Assert.True(await first);
		Assert.True(await second);
		Assert.Single(context.Interaction.HostTrustPrompts);
		Assert.Single(await KnownHosts(scope).ListAsync(Ct));
	}

	[Fact]
	public async Task VerifyAsync_SessionThatAskedGivesUp_WaitingSessionAsksForItself()
	{
		await using CoreTestContext context = CreateContext();
		await using AsyncServiceScope scope = await PrepareAsync(context, HostKeyPolicy.Ask);
		TaskCompletionSource userAnswers = new(TaskCreationOptions.RunContinuationsAsynchronously);
		context.Interaction.HostTrustDecision = HostTrustDecision.AcceptOnce;
		context.Interaction.HostTrustGate = userAnswers.Task;
		using CancellationTokenSource firstSession = CancellationTokenSource.CreateLinkedTokenSource(Ct);

		Task<bool> first = Verifier(scope).VerifyAsync(Identity, firstSession.Token).AsTask();
		await context.Interaction.HostTrustPromptShown.WaitAsync(Ct);
		Task<bool> second = Verifier(scope).VerifyAsync(Identity, Ct).AsTask();
		await Task.Delay(TimeSpan.FromMilliseconds(250), Ct);

		await firstSession.CancelAsync();
		_ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
		userAnswers.SetResult();

		Assert.True(await second);
		Assert.Equal(2, context.Interaction.HostTrustPrompts.Count);
	}

	private static CoreTestContext CreateContext() =>
		new(services => services.AddSingleton<IAppDataStore>(new InMemoryAppDataStore()));

	private static async Task<AsyncServiceScope> PrepareAsync(CoreTestContext context, HostKeyPolicy policy, HostIdentity? trusted = null)
	{
		await context.Services.GetRequiredService<ISettingsService>().UpdateAsync<SecuritySettings>(settings => settings with { HostKeyPolicy = policy });
		AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		if (trusted is not null)
		{
			await KnownHosts(scope).TrustAsync(trusted);
		}

		return scope;
	}

	private static IHostIdentityVerifier Verifier(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<IHostIdentityVerifier>();

	private static IKnownHostsStore KnownHosts(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<IKnownHostsStore>();

	private static HostIdentity Certificate(bool chainTrusted) => new()
	{
		Host = "ftp.example.com",
		Port = 990,
		Kind = HostIdentityKind.TlsCertificate,
		Algorithm = "RSA 2048",
		Fingerprint = "A1B2C3D4E5F6",
		Subject = "CN=ftp.example.com",
		ChainTrusted = chainTrusted,
	};
}
