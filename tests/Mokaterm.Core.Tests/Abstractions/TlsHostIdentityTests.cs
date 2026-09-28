using System.Globalization;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Mokaterm.Abstractions.Security;

namespace Mokaterm.Core.Tests.Abstractions;

public sealed class TlsHostIdentityTests
{
	private static readonly DateTimeOffset NotAfter = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);

	[Fact]
	public void Create_RsaCertificate_DescribesKeyFingerprintAndNames()
	{
		using X509Certificate2 certificate = Rsa("ftp.example.test");

		HostIdentity identity = TlsHostIdentity.Create("ftp.example.test", 990, certificate, SslPolicyErrors.RemoteCertificateChainErrors);

		Assert.Equal("ftp.example.test", identity.Host);
		Assert.Equal(990, identity.Port);
		Assert.Equal(HostIdentityKind.TlsCertificate, identity.Kind);
		Assert.Equal("RSA 2048", identity.Algorithm);
		Assert.Equal(ExpectedFingerprint(certificate), identity.Fingerprint);
		Assert.Equal("CN=ftp.example.test", identity.Subject);
		Assert.Equal("CN=ftp.example.test", identity.Issuer);
		Assert.Equal(NotAfter, identity.NotAfter);
		Assert.False(identity.ChainTrusted);
	}

	[Fact]
	public void Create_EcdsaCertificateWithoutPolicyErrors_IsChainTrusted()
	{
		using X509Certificate2 certificate = Ecdsa("files.example.test");

		HostIdentity identity = TlsHostIdentity.Create("files.example.test", 21, certificate, SslPolicyErrors.None);

		Assert.Equal("ECDSA 256", identity.Algorithm);
		Assert.True(identity.ChainTrusted);
	}

	[Fact]
	public void Create_TheBaseCertificateType_ReadsTheSameDetails()
	{
		using X509Certificate2 certificate = Rsa("base.example.test");

		// A validation callback may hand over the base type; the details live on X509Certificate2 only.
		X509Certificate plain = X509CertificateLoader.LoadCertificate(certificate.RawData);
		HostIdentity identity = TlsHostIdentity.Create("base.example.test", 8883, plain, chainTrusted: true);

		Assert.Equal("RSA 2048", identity.Algorithm);
		Assert.Equal(ExpectedFingerprint(certificate), identity.Fingerprint);
		Assert.Equal("CN=base.example.test", identity.Subject);
		Assert.True(identity.ChainTrusted);
	}

	[Fact]
	public void Fingerprint_IsUppercaseHexSha256OfTheDerBytes()
	{
		using X509Certificate2 certificate = Rsa("fingerprint.test", keySize: 1024);

		string fingerprint = TlsHostIdentity.Fingerprint(certificate);

		Assert.Matches("^[0-9A-F]{64}$", fingerprint);
		Assert.Equal(ExpectedFingerprint(certificate), fingerprint);
		Assert.Equal("RSA 1024", TlsHostIdentity.DescribePublicKey(certificate));
	}

	[Fact]
	public void Fingerprint_IsTheSameThroughEitherCertificateType()
	{
		using X509Certificate2 certificate = Rsa("either.test");
		X509Certificate plain = X509CertificateLoader.LoadCertificate(certificate.RawData);

		Assert.Equal(TlsHostIdentity.Fingerprint(certificate), TlsHostIdentity.Fingerprint(plain));
	}

	[Fact]
	public void Fingerprint_DiffersBetweenCertificatesForTheSameName()
	{
		using X509Certificate2 first = Rsa("same.test");
		using X509Certificate2 second = Rsa("same.test");

		Assert.NotEqual(TlsHostIdentity.Fingerprint(first), TlsHostIdentity.Fingerprint(second));
	}

	[Fact]
	public void IsChainTrusted_SelfSignedCertificate_IsNotTrusted()
	{
		using X509Certificate2 certificate = Rsa("self.example.test");

		Assert.False(TlsHostIdentity.IsChainTrusted(certificate, "self.example.test"));
	}

	[Fact]
	public void IsChainTrusted_AnotherHostsName_IsNotTrusted()
	{
		using X509Certificate2 certificate = Rsa("one.example.test");

		Assert.False(TlsHostIdentity.IsChainTrusted(certificate, "two.example.test"));
	}

	[Fact]
	public void IsChainTrusted_AHostThatIsNotAName_IsNotTrusted()
	{
		using X509Certificate2 certificate = Rsa("one.example.test");

		// MatchesHostname throws on this rather than answering, and a connection must not fail over it.
		Assert.False(TlsHostIdentity.IsChainTrusted(certificate, "not a host name"));
	}

	private static X509Certificate2 Rsa(string subject, int keySize = 2048) => TestCertificates.CreateRsa(subject, NotAfter, keySize);

	private static X509Certificate2 Ecdsa(string subject) => TestCertificates.CreateEcdsa(subject, NotAfter);

	private static string ExpectedFingerprint(X509Certificate2 certificate) =>
		string.Concat(SHA256.HashData(certificate.RawData).Select(value => value.ToString("X2", CultureInfo.InvariantCulture)));
}
