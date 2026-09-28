using System.Globalization;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Mokaterm.Abstractions.Security;

/// <summary>
/// Turns a server's TLS certificate into a <see cref="HostIdentity"/> for <see cref="IHostIdentityVerifier"/>. Every
/// protocol that meets a certificate asks the same questions of it, and the fingerprint in particular has to be the
/// same string everywhere or a host trusted over one protocol would look new over another.
/// </summary>
public static class TlsHostIdentity
{
	/// <summary>Uppercase hex SHA-256 of the DER bytes, which is what the known hosts store keeps.</summary>
	public static string Fingerprint(X509Certificate certificate)
	{
		ArgumentNullException.ThrowIfNull(certificate);
		return certificate is X509Certificate2 certificate2
			? Convert.ToHexString(SHA256.HashData(certificate2.RawDataMemory.Span))
			: Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData()));
	}

	/// <summary>Key type and size, such as <c>RSA 2048</c> or <c>ECDSA 256</c>.</summary>
	public static string DescribePublicKey(X509Certificate2 certificate)
	{
		ArgumentNullException.ThrowIfNull(certificate);
		using (RSA? rsa = certificate.GetRSAPublicKey())
		{
			if (rsa is not null)
			{
				return string.Create(CultureInfo.InvariantCulture, $"RSA {rsa.KeySize}");
			}
		}

		using (ECDsa? ecdsa = certificate.GetECDsaPublicKey())
		{
			if (ecdsa is not null)
			{
				return string.Create(CultureInfo.InvariantCulture, $"ECDSA {ecdsa.KeySize}");
			}
		}

		// .NET has no public key object for these, so the curve is read from the algorithm's own identifier.
		Oid oid = certificate.PublicKey.Oid;
		return oid.Value switch
		{
			"1.3.101.112" => "Ed25519",
			"1.3.101.113" => "Ed448",
			_ => oid.FriendlyName ?? oid.Value ?? "Unknown",
		};
	}

	/// <summary>The identity of a certificate whose chain the platform already judged, as in a validation callback.</summary>
	public static HostIdentity Create(string host, int port, X509Certificate certificate, SslPolicyErrors policyErrors) =>
		Create(host, port, certificate, policyErrors == SslPolicyErrors.None);

	/// <summary>The identity of a certificate, with the caller saying whether the chain is trusted for this host.</summary>
	public static HostIdentity Create(string host, int port, X509Certificate certificate, bool chainTrusted)
	{
		ArgumentNullException.ThrowIfNull(certificate);
		if (certificate is X509Certificate2 certificate2)
		{
			return Describe(host, port, certificate2, chainTrusted);
		}

		// A callback may hand over the base type; the details below live on X509Certificate2 only.
		using X509Certificate2 loaded = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
		return Describe(host, port, loaded, chainTrusted);
	}

	/// <summary>
	/// Whether the platform trusts this certificate for this host. For a caller that ran the handshake itself and so
	/// never got a verdict, such as a library that accepts every certificate on its own.
	/// </summary>
	public static bool IsChainTrusted(X509Certificate2 certificate, string host)
	{
		ArgumentNullException.ThrowIfNull(certificate);
		try
		{
			if (!certificate.MatchesHostname(host, allowWildcards: true, allowCommonName: true))
			{
				return false;
			}

			using X509Chain chain = new();

			// A revocation list lives behind a URL the machine may well not reach, and the lookup would hold the
			// connection open while it times out.
			chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
			return chain.Build(certificate);
		}
		catch (Exception ex) when (ex is ArgumentException or CryptographicException)
		{
			// A host that is not a name or an address, or a certificate the platform cannot read: not trusted.
			return false;
		}
	}

	/// <summary>The certificate the other end of a finished handshake presented, or null when it sent none.</summary>
	public static X509Certificate2? Read(SslStream stream)
	{
		ArgumentNullException.ThrowIfNull(stream);
		X509Certificate? remote = stream.RemoteCertificate;
		return remote is null ? null : X509CertificateLoader.LoadCertificate(remote.Export(X509ContentType.Cert));
	}

	private static HostIdentity Describe(string host, int port, X509Certificate2 certificate, bool chainTrusted) => new()
	{
		Host = host,
		Port = port,
		Kind = HostIdentityKind.TlsCertificate,
		Algorithm = DescribePublicKey(certificate),
		Fingerprint = Fingerprint(certificate),
		Subject = certificate.Subject,
		Issuer = certificate.Issuer,
		NotAfter = new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero),
		ChainTrusted = chainTrusted,
	};
}
