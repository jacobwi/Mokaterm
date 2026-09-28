using System.Diagnostics.CodeAnalysis;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;

namespace Mokaterm.Modules.Vnc.Protocol;

/// <summary>Wraps a VeNCrypt connection in TLS and asks the host verifier about the certificate it presented.</summary>
internal static class VncTlsUpgrade
{
	/// <summary>
	/// Runs the TLS handshake and, for the X509 subtypes, the trust decision. The certificate is accepted during the
	/// handshake and judged right after: nothing is sent until the verifier has answered, so a rejected server never
	/// sees a password.
	/// </summary>
	/// <exception cref="ProtocolConnectException">The handshake failed or the certificate was rejected.</exception>
	[SuppressMessage(
		"Security",
		"CA5359:Do Not Disable Certificate Validation",
		Justification = "The certificate is judged by IHostIdentityVerifier right after the handshake and before a single byte is sent; VNC servers use self-signed certificates, which no chain check can accept.")]
	public static async Task<SslStream> AuthenticateAsync(
		Stream inner,
		string host,
		int port,
		bool expectCertificate,
		IHostIdentityVerifier verifier,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(inner);
		ArgumentNullException.ThrowIfNull(verifier);

		X509Certificate2? presented = null;
		SslPolicyErrors policyErrors = SslPolicyErrors.None;
		SslStream tls = new(inner, leaveInnerStreamOpen: true, (_, certificate, _, errors) =>
		{
			policyErrors = errors;

			// A copy: the certificate the callback gets belongs to the SslStream and dies with it.
			presented = certificate is null ? null : X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
			return true;
		});

		try
		{
			await tls.AuthenticateAsClientAsync(
				new SslClientAuthenticationOptions
				{
					TargetHost = host,

					// The operating system picks the protocol versions; VNC certificates are usually self-signed and
					// have no revocation list to check.
					EnabledSslProtocols = SslProtocols.None,
					CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
				},
				cancellationToken);

			await VerifyAsync(presented, policyErrors, host, port, expectCertificate, verifier, cancellationToken);
			return tls;
		}
		catch (Exception ex) when (ex is AuthenticationException or IOException)
		{
			await tls.DisposeAsync();
			presented?.Dispose();
			throw new ProtocolConnectException(ConnectFailure.ProtocolError, DescribeFailure(host, port, expectCertificate, ex), ex);
		}
		catch
		{
			await tls.DisposeAsync();
			presented?.Dispose();
			throw;
		}
	}

	private static async Task VerifyAsync(
		X509Certificate2? presented,
		SslPolicyErrors policyErrors,
		string host,
		int port,
		bool expectCertificate,
		IHostIdentityVerifier verifier,
		CancellationToken cancellationToken)
	{
		if (!expectCertificate)
		{
			// Anonymous VeNCrypt has no identity to check: the tunnel keeps the session off the wire, nothing more.
			presented?.Dispose();
			return;
		}

		string endpoint = HostEndpoint.Format(host, port);
		if (presented is null)
		{
			throw new ProtocolConnectException(
				ConnectFailure.ProtocolError,
				$"{endpoint} chose a certificate based VeNCrypt type but sent no certificate.");
		}

		using (presented)
		{
			HostIdentity identity = TlsHostIdentity.Create(host, port, presented, policyErrors);
			if (!await verifier.VerifyAsync(identity, cancellationToken))
			{
				throw new ProtocolConnectException(
					ConnectFailure.HostIdentityRejected,
					$"The TLS certificate presented by {endpoint} was not trusted.");
			}
		}
	}

	private static string DescribeFailure(string host, int port, bool expectCertificate, Exception exception)
	{
		string endpoint = HostEndpoint.Format(host, port);
		return expectCertificate
			? $"The TLS handshake with {endpoint} failed: {exception.Message}"

			// Anonymous VeNCrypt needs the TLS_DH_anon cipher suites, which Windows and .NET do not offer.
			: $"{endpoint} offered only anonymous TLS (VeNCrypt TLS types), which this platform cannot negotiate. Configure the server with a certificate (X509 types).";
	}
}
