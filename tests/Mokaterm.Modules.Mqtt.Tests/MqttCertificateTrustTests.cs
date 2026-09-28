using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging.Abstractions;
using Mokaterm.Abstractions.Security;
using Mokaterm.Modules.Mqtt.Protocol;
using MQTTnet;

namespace Mokaterm.Modules.Mqtt.Tests;

public sealed class MqttCertificateTrustTests
{
	[Fact]
	public void Validate_WithAnApprovingVerifier_TakesTheCertificateInsideTheHandshake()
	{
		using X509Certificate2 certificate = CreateCertificate();
		FakeHostVerifier verifier = FakeHostVerifier.Accepting();
		using MqttCertificateTrust trust = Create(verifier);

		Assert.True(trust.Validate(Args(certificate)));
		Assert.Equal(0, trust.RefusalCount);

		HostIdentity asked = Assert.Single(verifier.Seen);
		Assert.Equal(HostIdentityKind.TlsCertificate, asked.Kind);
		Assert.Equal("broker.test", asked.Host);
		Assert.Equal(8883, asked.Port);
		Assert.Equal(Convert.ToHexString(SHA256.HashData(certificate.RawDataMemory.Span)), asked.Fingerprint);
		Assert.StartsWith("RSA ", asked.Algorithm, StringComparison.Ordinal);
		Assert.False(asked.ChainTrusted);
	}

	[Fact]
	public void Validate_AsksOnceForTheSameCertificate()
	{
		using X509Certificate2 certificate = CreateCertificate();
		FakeHostVerifier verifier = FakeHostVerifier.Accepting();
		using MqttCertificateTrust trust = Create(verifier);

		Assert.True(trust.Validate(Args(certificate)));
		Assert.True(trust.Validate(Args(certificate)));

		Assert.Single(verifier.Seen);
	}

	[Fact]
	public void Validate_WithARefusingVerifier_RefusesTheHandshakeSoNoConnectPacketGoesOut()
	{
		using X509Certificate2 certificate = CreateCertificate();
		using MqttCertificateTrust trust = Create(FakeHostVerifier.Rejecting());

		Assert.False(trust.Validate(Args(certificate)));
		Assert.Equal(1, trust.RefusalCount);
		Assert.NotNull(trust.LastRefused);
	}

	[Fact]
	public async Task Validate_WhileTheUserIsStillBeingAsked_RefusesAndLeavesTheDecisionOpen()
	{
		using X509Certificate2 certificate = CreateCertificate();
		using MqttCertificateTrust trust = Create(FakeHostVerifier.AnsweringAfter(TimeSpan.FromMilliseconds(400), answer: true), wait: TimeSpan.FromMilliseconds(20));

		Assert.False(trust.Validate(Args(certificate)));

		MqttCertificateDecision refused = Assert.IsType<MqttCertificateDecision>(trust.LastRefused);
		Assert.True(await refused.WaitAsync(TestContext.Current.CancellationToken));

		// The connect loop approves the fingerprint the answer came back for, and the next handshake takes it.
		trust.Approve(refused.Identity.Fingerprint);
		Assert.True(trust.Validate(Args(certificate)));
	}

	[Fact]
	public void Validate_WithNoCertificateAtAll_Refuses()
	{
		using MqttCertificateTrust trust = Create(FakeHostVerifier.Accepting());

		Assert.False(trust.Validate(Args(certificate: null)));
	}

	[Fact]
	public void Validate_AfterDispose_Refuses()
	{
		using X509Certificate2 certificate = CreateCertificate();
		MqttCertificateTrust trust = Create(FakeHostVerifier.Accepting());

		trust.Dispose();

		Assert.False(trust.Validate(Args(certificate)));
	}

	[Fact]
	public void Validate_WhenTheVerifierThrows_CountsAsARefusal()
	{
		using X509Certificate2 certificate = CreateCertificate();
		using MqttCertificateTrust trust = Create(new ThrowingVerifier());

		Assert.False(trust.Validate(Args(certificate)));
		Assert.Equal(1, trust.RefusalCount);
	}

	[Fact]
	public void Identity_ReportsAChainTheOperatingSystemTrusts()
	{
		using X509Certificate2 certificate = CreateCertificate();

		HostIdentity identity = TlsHostIdentity.Create("broker.test", 8883, certificate, SslPolicyErrors.None);

		Assert.True(identity.ChainTrusted);
		Assert.Equal(certificate.Subject, identity.Subject);
		Assert.Equal(certificate.Issuer, identity.Issuer);
		Assert.NotNull(identity.NotAfter);
	}

	[Fact]
	public void Identity_DescribesAnEcdsaKeyByItsSize()
	{
		using X509Certificate2 certificate = TestCertificates.CreateEcdsa("ec.test", DateTimeOffset.UtcNow.AddDays(1));

		Assert.Equal("ECDSA 256", TlsHostIdentity.DescribePublicKey(certificate));
	}

	[Fact]
	public void Fingerprint_IsTheUppercaseHexSha256OfTheCertificate()
	{
		using X509Certificate2 certificate = CreateCertificate();

		Assert.Equal(
			Convert.ToHexString(SHA256.HashData(certificate.RawDataMemory.Span)),
			TlsHostIdentity.Fingerprint(certificate));
	}

	private static MqttCertificateTrust Create(IHostIdentityVerifier verifier, TimeSpan? wait = null) =>
		new(verifier, "broker.test", 8883, NullLogger.Instance, wait ?? TimeSpan.FromSeconds(2));

	// The MQTTnet event args check their channel options for null, so a real one stands in for the handshake's.
	private static MqttClientCertificateValidationEventArgs Args(X509Certificate2? certificate) =>
		new(certificate!, chain: null!, SslPolicyErrors.RemoteCertificateChainErrors, new MqttClientTcpOptions());

	private static X509Certificate2 CreateCertificate() =>
		TestCertificates.CreateRsa("broker.test", DateTimeOffset.UtcNow.AddDays(30));

	private sealed class ThrowingVerifier : IHostIdentityVerifier
	{
		public ValueTask<bool> VerifyAsync(HostIdentity identity, CancellationToken cancellationToken = default) =>
			throw new InvalidOperationException("The vault is locked.");
	}
}
