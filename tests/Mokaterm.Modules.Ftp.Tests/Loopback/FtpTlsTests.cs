using System.Security.Cryptography.X509Certificates;
using System.Text;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;
using Mokaterm.Modules.Ftp.Connection;
using Mokaterm.Modules.Ftp.Tests.Fakes;

namespace Mokaterm.Modules.Ftp.Tests.Loopback;

public sealed class FtpTlsTests : IDisposable
{
	private readonly X509Certificate2 _certificate = TestCertificates.CreateServerCertificate();

	public void Dispose() => _certificate.Dispose();

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task ExplicitTls_CertificateTrustedQuickly_ConnectsInOneHandshakeAndEncryptsData()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackFtpServer server = FtpHarness.StartServer(new LoopbackFtpServerOptions { Certificate = _certificate });
		FakeHostVerifier verifier = FakeHostVerifier.Accepting();

		await using IProtocolSession session = await ConnectAsync(server, FtpEncryption.Explicit, verifier, cancellationToken);
		Assert.Equal(1, server.TotalConnections);
		IRemoteFileSystem files = await FtpHarness.OpenFileSystemAsync(session, cancellationToken);
		IReadOnlyList<RemoteFileEntry> entries = await files.ListAsync(FtpHarness.Home, cancellationToken);
		using MemoryStream notes = new();
		await files.DownloadAsync(FtpHarness.NotesPath, notes, null, cancellationToken);

		HostIdentity identity = Assert.Single(verifier.Seen);
		Assert.Equal(HostIdentityKind.TlsCertificate, identity.Kind);
		Assert.Equal("127.0.0.1", identity.Host);
		Assert.Equal(server.Port, identity.Port);
		Assert.Equal(TlsHostIdentity.Fingerprint(_certificate), identity.Fingerprint);
		Assert.Equal("RSA 2048", identity.Algorithm);
		Assert.Equal("CN=localhost", identity.Subject);
		Assert.False(identity.ChainTrusted);
		Assert.Contains(entries, entry => entry.Name == "notes.txt");
		Assert.Equal(FtpHarness.NotesText, Encoding.UTF8.GetString(notes.ToArray()));
		Assert.Contains("PROT P", server.Commands);
	}

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task ExplicitTls_SlowApproval_ReconnectsWithTheApprovedCertificate()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackFtpServer server = FtpHarness.StartServer(new LoopbackFtpServerOptions { Certificate = _certificate });
		FakeHostVerifier verifier = FakeHostVerifier.AnsweringAfter(FtpHarness.CertificateDecisionWait * 4, answer: true);
		RecordingProgress<string> status = new();

		await using IProtocolSession session = await FtpHarness.CreateProvider().ConnectAsync(
			FtpHarness.CreateContext(server.Port, verifier: verifier, options: Options(FtpEncryption.Explicit), status: status),
			cancellationToken);
		IRemoteFileSystem files = await FtpHarness.OpenFileSystemAsync(session, cancellationToken);
		await files.DownloadAsync(FtpHarness.NotesPath, new MemoryStream(), null, cancellationToken);

		Assert.Single(verifier.Seen);
		Assert.Contains("Waiting for the certificate to be approved", status.Reports);
		Assert.Equal(3, server.TotalConnections);
	}

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task ExplicitTls_RejectedCertificate_FailsWithHostIdentityRejected()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackFtpServer server = FtpHarness.StartServer(new LoopbackFtpServerOptions { Certificate = _certificate });

		ProtocolConnectException exception = await Assert.ThrowsAsync<ProtocolConnectException>(() =>
			ConnectAsync(server, FtpEncryption.Explicit, FakeHostVerifier.Rejecting(), cancellationToken));

		Assert.Equal(ConnectFailure.HostIdentityRejected, exception.Failure);
	}

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task ExplicitTls_SlowRejection_FailsWithHostIdentityRejected()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackFtpServer server = FtpHarness.StartServer(new LoopbackFtpServerOptions { Certificate = _certificate });
		FakeHostVerifier verifier = FakeHostVerifier.AnsweringAfter(FtpHarness.CertificateDecisionWait * 4, answer: false);

		ProtocolConnectException exception = await Assert.ThrowsAsync<ProtocolConnectException>(() =>
			ConnectAsync(server, FtpEncryption.Explicit, verifier, cancellationToken));

		Assert.Equal(ConnectFailure.HostIdentityRejected, exception.Failure);
		Assert.Single(verifier.Seen);
	}

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task ImplicitTls_Connects()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackFtpServer server = FtpHarness.StartServer(new LoopbackFtpServerOptions { Certificate = _certificate, ImplicitTls = true });
		FakeHostVerifier verifier = FakeHostVerifier.Accepting();

		await using IProtocolSession session = await ConnectAsync(server, FtpEncryption.Implicit, verifier, cancellationToken);
		IRemoteFileSystem files = await FtpHarness.OpenFileSystemAsync(session, cancellationToken);

		Assert.Contains(await files.ListAsync(FtpHarness.Home, cancellationToken), entry => entry.Name == "notes.txt");
		Assert.Single(verifier.Seen);
		Assert.DoesNotContain("AUTH TLS", server.Commands);
	}

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task ExplicitTls_ServerWithoutTls_FailsWithProtocolError()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackFtpServer server = FtpHarness.StartServer();

		ProtocolConnectException exception = await Assert.ThrowsAsync<ProtocolConnectException>(() =>
			ConnectAsync(server, FtpEncryption.Explicit, FakeHostVerifier.Accepting(), cancellationToken));

		Assert.Equal(ConnectFailure.ProtocolError, exception.Failure);
		Assert.Contains("AUTH TLS", exception.Message, StringComparison.Ordinal);
	}

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task ImplicitTls_AgainstPlainServer_FailsWithProtocolError()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackFtpServer server = FtpHarness.StartServer();

		ProtocolConnectException exception = await Assert.ThrowsAsync<ProtocolConnectException>(() =>
			ConnectAsync(server, FtpEncryption.Implicit, FakeHostVerifier.Accepting(), cancellationToken));

		Assert.Equal(ConnectFailure.ProtocolError, exception.Failure);
	}

	private static ProtocolOptions Options(FtpEncryption encryption) => new FtpConnectionOptions { Encryption = encryption }.ApplyTo(ProtocolOptions.Empty);

	private static Task<IProtocolSession> ConnectAsync(LoopbackFtpServer server, FtpEncryption encryption, FakeHostVerifier verifier, CancellationToken cancellationToken) =>
		FtpHarness.CreateProvider().ConnectAsync(FtpHarness.CreateContext(server.Port, verifier: verifier, options: Options(encryption)), cancellationToken);
}
