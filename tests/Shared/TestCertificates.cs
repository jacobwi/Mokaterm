using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Mokaterm.Tests.Shared;

/// <summary>Self-signed certificates made on the fly, like the ones an FTPS or VeNCrypt server ships with.</summary>
internal static class TestCertificates
{
	public static X509Certificate2 CreateRsa(string subject, DateTimeOffset notAfter, int keySize = 2048)
	{
		using RSA rsa = RSA.Create(keySize);
		CertificateRequest request = new($"CN={subject}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
		return request.CreateSelfSigned(notAfter.AddYears(-1), notAfter);
	}

	public static X509Certificate2 CreateEcdsa(string subject, DateTimeOffset notAfter)
	{
		using ECDsa ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
		CertificateRequest request = new($"CN={subject}", ecdsa, HashAlgorithmName.SHA256);
		return request.CreateSelfSigned(notAfter.AddYears(-1), notAfter);
	}

	/// <summary>
	/// A certificate a TLS server can use. SChannel cannot sign with the ephemeral key CreateSelfSigned returns, so it
	/// goes through PKCS#12. The subject alternative name follows <paramref name="subject"/>, plus the loopback address
	/// the test servers listen on: a SAN that named something else would contradict the CN.
	/// </summary>
	public static X509Certificate2 CreateServerCertificate(string subject = "localhost")
	{
		using RSA rsa = RSA.Create(2048);
		CertificateRequest request = new($"CN={subject}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
		request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, critical: false));
		request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], critical: false));
		SubjectAlternativeNameBuilder names = new();
		names.AddDnsName(subject);
		names.AddIpAddress(IPAddress.Loopback);
		request.CertificateExtensions.Add(names.Build());

		using X509Certificate2 ephemeral = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
		return X509CertificateLoader.LoadPkcs12(ephemeral.Export(X509ContentType.Pkcs12), password: null);
	}
}
