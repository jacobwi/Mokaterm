using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using FluentFTP;
using Microsoft.Extensions.Logging.Abstractions;
using Mokaterm.Abstractions.Security;
using Mokaterm.Modules.Ftp.Connection;
using Mokaterm.Modules.Ftp.Tests.Fakes;

namespace Mokaterm.Modules.Ftp.Tests;

public sealed class FtpCertificateTrustTests : IDisposable
{
	private static readonly TimeSpan ShortWait = TimeSpan.FromMilliseconds(100);

	private readonly X509Certificate2 _certificate = TestCertificates.CreateRsa("trust.test", DateTimeOffset.UtcNow.AddYears(1));

	public void Dispose() => _certificate.Dispose();

	[Fact]
	public void Validate_ApprovedFingerprint_AcceptsWithoutAsking()
	{
		FakeHostVerifier verifier = FakeHostVerifier.Rejecting();
		using FtpCertificateTrust trust = CreateTrust(verifier);
		trust.Approve(TlsHostIdentity.Fingerprint(_certificate));

		Assert.True(trust.Validate(_certificate, SslPolicyErrors.RemoteCertificateChainErrors));
		Assert.Empty(verifier.Seen);
	}

	[Fact]
	public void Validate_QuickApproval_AcceptsInTheSameHandshakeAndRemembersIt()
	{
		FakeHostVerifier verifier = FakeHostVerifier.Accepting();
		using FtpCertificateTrust trust = CreateTrust(verifier, TimeSpan.FromSeconds(10));

		Assert.True(trust.Validate(_certificate, SslPolicyErrors.RemoteCertificateChainErrors));
		Assert.True(trust.Validate(_certificate, SslPolicyErrors.RemoteCertificateChainErrors));

		HostIdentityAssert(verifier);
		Assert.Equal(TlsHostIdentity.Fingerprint(_certificate), trust.ApprovedFingerprint);
		Assert.Equal(0, trust.RefusalCount);
	}

	[Fact]
	public void Validate_Rejection_RefusesAndRecordsTheDecision()
	{
		using FtpCertificateTrust trust = CreateTrust(FakeHostVerifier.Rejecting(), TimeSpan.FromSeconds(10));

		Assert.False(trust.Validate(_certificate, SslPolicyErrors.None));

		Assert.Equal(1, trust.RefusalCount);
		Assert.NotNull(trust.LastRefused);
		Assert.True(trust.LastRefused.IsDecided);
		Assert.False(trust.LastRefused.IsApproved);
		Assert.Null(trust.ApprovedFingerprint);
	}

	[Fact]
	public async Task Validate_SlowDecision_RefusesThenAcceptsOnceApproved()
	{
		TaskCompletionSource<bool> answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
		FakeHostVerifier verifier = new() { Decide = (_, _) => answer.Task };
		using FtpCertificateTrust trust = CreateTrust(verifier);

		Assert.False(trust.Validate(_certificate, SslPolicyErrors.RemoteCertificateChainErrors));
		FtpCertificateDecision? pending = trust.LastRefused;
		Assert.NotNull(pending);
		Assert.False(pending.IsDecided);

		answer.SetResult(true);
		Assert.True(await pending.WaitAsync(TestContext.Current.CancellationToken));
		trust.Approve(pending.Identity.Fingerprint);

		Assert.True(trust.Validate(_certificate, SslPolicyErrors.RemoteCertificateChainErrors));
		Assert.Single(verifier.Seen);
	}

	[Fact]
	public void Validate_SameCertificateWhileUndecided_AsksOnlyOnce()
	{
		TaskCompletionSource<bool> answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
		FakeHostVerifier verifier = new() { Decide = (_, _) => answer.Task };
		using FtpCertificateTrust trust = CreateTrust(verifier);

		Assert.False(trust.Validate(_certificate, SslPolicyErrors.None));
		Assert.False(trust.Validate(_certificate, SslPolicyErrors.None));

		Assert.Single(verifier.Seen);
		Assert.Equal(2, trust.RefusalCount);
		answer.SetResult(false);
	}

	[Fact]
	public void Validate_VerifierFailure_CountsAsRejection()
	{
		FakeHostVerifier verifier = new() { Decide = static (_, _) => Task.FromException<bool>(new InvalidOperationException("vault locked")) };
		using FtpCertificateTrust trust = CreateTrust(verifier, TimeSpan.FromSeconds(10));

		Assert.False(trust.Validate(_certificate, SslPolicyErrors.None));
		Assert.NotNull(trust.LastRefused);
		Assert.True(trust.LastRefused.IsDecided);
		Assert.False(trust.LastRefused.IsApproved);
	}

	[Fact]
	public async Task Dispose_CancelsAnOpenPrompt()
	{
		FakeHostVerifier verifier = new()
		{
			Decide = static async (_, cancellationToken) =>
			{
				await Task.Delay(Timeout.Infinite, cancellationToken);
				return true;
			},
		};
		FtpCertificateTrust trust = CreateTrust(verifier);
		Assert.False(trust.Validate(_certificate, SslPolicyErrors.None));
		FtpCertificateDecision? pending = trust.LastRefused;
		Assert.NotNull(pending);

		trust.Dispose();

		Assert.False(await pending.WaitAsync(TestContext.Current.CancellationToken));
		Assert.False(trust.Validate(_certificate, SslPolicyErrors.None));
	}

	[Fact]
	public void OnValidateCertificate_OverwritesFluentFtpsDefaultAccept()
	{
		using FtpCertificateTrust trust = CreateTrust(FakeHostVerifier.Rejecting(), TimeSpan.FromSeconds(10));
		using AsyncFtpClient client = new();
		FtpSslValidationEventArgs args = new() { Certificate = _certificate, PolicyErrors = SslPolicyErrors.None, Accept = true };

		trust.OnValidateCertificate(client, args);

		Assert.False(args.Accept);
	}

	private static FtpCertificateTrust CreateTrust(FakeHostVerifier verifier, TimeSpan? wait = null) =>
		new(verifier, "trust.test", 21, NullLogger.Instance, wait ?? ShortWait);

	private void HostIdentityAssert(FakeHostVerifier verifier)
	{
		Assert.True(verifier.Seen.TryPeek(out Abstractions.Security.HostIdentity? identity));
		Assert.Single(verifier.Seen);
		Assert.Equal("trust.test", identity.Host);
		Assert.Equal(21, identity.Port);
		Assert.Equal(TlsHostIdentity.Fingerprint(_certificate), identity.Fingerprint);
	}
}
