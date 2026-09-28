using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;
using Mokaterm.Modules.Vnc.Protocol;
using Mokaterm.Modules.Vnc.Tests.Fakes;

namespace Mokaterm.Modules.Vnc.Tests.Loopback;

/// <summary>The handshake against the loopback server: versions, security negotiation, authentication and TLS.</summary>
public sealed class VncConnectTests
{
	[Fact(Timeout = VncHarness.TestTimeoutMilliseconds)]
	public async Task Connect_SecurityNone_ReportsTheScreenUnencrypted()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackVncServer server = VncHarness.StartServer(new LoopbackVncServerOptions
		{
			SecurityTypes = [(int)VncSecurityType.None],
			DesktopName = "demo :1",
			Width = 1024,
			Height = 768,
		});

		RecordingProgress<string> status = new();
		ProtocolConnectContext context = VncHarness.CreateContext(
			server.Port,
			new FakeCredentialSource(null, null, AuthenticationMethod.Anonymous),
			status: status);

		(IProtocolSession session, IVncConnection connection) = await VncHarness.ConnectAsync(VncHarness.CreateProvider(), context, cancellationToken);
		await using (session)
		{
			LoopbackVncConnection accepted = await server.AcceptedAsync(cancellationToken);

			Assert.Equal("RFB 003.008\n", accepted.ClientVersion);
			Assert.Equal((int)VncSecurityType.None, accepted.ChosenSecurity);
			Assert.Equal<byte?>(1, accepted.SharedFlag);
			Assert.Equal("demo :1", connection.Info.DesktopName);
			Assert.Equal(1024, connection.Info.Width);
			Assert.Equal(768, connection.Info.Height);
			Assert.False(connection.Info.IsEncrypted);
			Assert.Contains("Connecting", status.Reports);
		}
	}

	[Fact(Timeout = VncHarness.TestTimeoutMilliseconds)]
	public async Task Connect_NotShared_SendsTheClientInitFlagFromTheOptions()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackVncServer server = VncHarness.StartServer();
		ProtocolConnectContext context = VncHarness.CreateContext(
			server.Port,
			new FakeCredentialSource(null, null, AuthenticationMethod.Anonymous),
			options: VncConnectionOptions.Default with { Shared = false });

		await using IProtocolSession session = await VncHarness.CreateProvider().ConnectAsync(context, cancellationToken);
		LoopbackVncConnection accepted = await server.AcceptedAsync(cancellationToken);

		Assert.Equal<byte?>(0, accepted.SharedFlag);
	}

	[Fact(Timeout = VncHarness.TestTimeoutMilliseconds)]
	public async Task Connect_VncAuthentication_AnswersTheChallenge()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackVncServer server = VncHarness.StartServer(new LoopbackVncServerOptions
		{
			SecurityTypes = [(int)VncSecurityType.VncAuth],
			Password = VncHarness.Password,
		});

		FakeCredentialSource credentials = new(null, VncHarness.Password);
		await using IProtocolSession session = await VncHarness.CreateProvider()
			.ConnectAsync(VncHarness.CreateContext(server.Port, credentials), cancellationToken);
		LoopbackVncConnection accepted = await server.AcceptedAsync(cancellationToken);

		Assert.True(accepted.PasswordAccepted);
		Assert.Equal((int)VncSecurityType.VncAuth, accepted.ChosenSecurity);
		Assert.Equal(1, credentials.GetCount);
		Assert.Empty(credentials.RetryReasons);
	}

	[Fact(Timeout = VncHarness.TestTimeoutMilliseconds)]
	public async Task Connect_WrongPassword_AsksAgainAndDialsOnceMore()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackVncServer server = VncHarness.StartServer(new LoopbackVncServerOptions
		{
			SecurityTypes = [(int)VncSecurityType.VncAuth],
			Password = VncHarness.Password,
		});

		FakeCredentialSource credentials = new(null, "wrong", AuthenticationMethod.Password, VncHarness.Password);
		await using IProtocolSession session = await VncHarness.CreateProvider()
			.ConnectAsync(VncHarness.CreateContext(server.Port, credentials), cancellationToken);
		LoopbackVncConnection accepted = await server.AcceptedAsync(cancellationToken);

		Assert.True(accepted.PasswordAccepted);
		Assert.Equal(2, server.Attempts);
		string reason = Assert.Single(credentials.RetryReasons);
		Assert.Contains("refused the login", reason, StringComparison.Ordinal);
	}

	[Fact(Timeout = VncHarness.TestTimeoutMilliseconds)]
	public async Task Connect_WrongPasswordAndNoRetry_FailsAsAuthentication()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackVncServer server = VncHarness.StartServer(new LoopbackVncServerOptions
		{
			SecurityTypes = [(int)VncSecurityType.VncAuth],
			Password = VncHarness.Password,
		});

		FakeCredentialSource credentials = new(null, "wrong");
		ProtocolConnectException failure = await Assert.ThrowsAsync<ProtocolConnectException>(
			() => VncHarness.CreateProvider().ConnectAsync(VncHarness.CreateContext(server.Port, credentials), cancellationToken));

		Assert.Equal(ConnectFailure.AuthenticationFailed, failure.Failure);
		Assert.Single(credentials.RetryReasons);
	}

	[Fact(Timeout = VncHarness.TestTimeoutMilliseconds)]
	public async Task Connect_BlockedByTheServer_DoesNotAskForAnotherPassword()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackVncServer server = VncHarness.StartServer(new LoopbackVncServerOptions
		{
			SecurityTypes = [(int)VncSecurityType.VncAuth],
			Password = VncHarness.Password,
			ForcedSecurityResult = 2,
		});

		FakeCredentialSource credentials = new(null, VncHarness.Password, AuthenticationMethod.Password, VncHarness.Password);
		ProtocolConnectException failure = await Assert.ThrowsAsync<ProtocolConnectException>(
			() => VncHarness.CreateProvider().ConnectAsync(VncHarness.CreateContext(server.Port, credentials), cancellationToken));

		Assert.Equal(ConnectFailure.AuthenticationFailed, failure.Failure);
		Assert.Empty(credentials.RetryReasons);
		Assert.Equal(1, server.Attempts);
	}

	[Fact(Timeout = VncHarness.TestTimeoutMilliseconds)]
	public async Task Connect_Rfb33_UsesTheSecurityTypeTheServerDictates()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackVncServer server = VncHarness.StartServer(new LoopbackVncServerOptions
		{
			Greeting = "RFB 003.003\n",
			SecurityTypes = [(int)VncSecurityType.VncAuth],
			Password = VncHarness.Password,
		});

		await using IProtocolSession session = await VncHarness.CreateProvider()
			.ConnectAsync(VncHarness.CreateContext(server.Port, new FakeCredentialSource(null, VncHarness.Password)), cancellationToken);
		LoopbackVncConnection accepted = await server.AcceptedAsync(cancellationToken);

		Assert.Equal("RFB 003.003\n", accepted.ClientVersion);
		Assert.True(accepted.PasswordAccepted);
	}

	[Fact(Timeout = VncHarness.TestTimeoutMilliseconds)]
	public async Task Connect_VeNCryptX509Vnc_VerifiesTheCertificateAndEncryptsTheSession()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		using X509Certificate2 certificate = TestCertificates.CreateServerCertificate();
		string fingerprint = Convert.ToHexString(SHA256.HashData(certificate.RawDataMemory.Span));
		await using LoopbackVncServer server = VncHarness.StartServer(new LoopbackVncServerOptions
		{
			SecurityTypes = [(int)VncSecurityType.VeNCrypt],
			VeNCryptSubtypes = [(int)VncSecurityType.X509Vnc],
			Certificate = certificate,
			Password = VncHarness.Password,
		});

		FakeHostVerifier verifier = FakeHostVerifier.Accepting();
		(IProtocolSession session, IVncConnection connection) = await VncHarness.ConnectAsync(
			VncHarness.CreateProvider(),
			VncHarness.CreateContext(server.Port, new FakeCredentialSource(null, VncHarness.Password), verifier),
			cancellationToken);
		await using (session)
		{
			LoopbackVncConnection accepted = await server.AcceptedAsync(cancellationToken);

			Assert.Equal((int)VncSecurityType.X509Vnc, accepted.ChosenSubtype);
			Assert.True(accepted.IsEncrypted);
			Assert.True(accepted.PasswordAccepted);
			Assert.True(connection.Info.IsEncrypted);
			Assert.Contains("TLS", connection.Info.Security, StringComparison.Ordinal);
			HostIdentity identity = Assert.Single(verifier.Seen);
			Assert.Equal(HostIdentityKind.TlsCertificate, identity.Kind);
			Assert.Equal(fingerprint, identity.Fingerprint);
			Assert.Equal(server.Port, identity.Port);
			Assert.False(identity.ChainTrusted);
		}
	}

	[Fact(Timeout = VncHarness.TestTimeoutMilliseconds)]
	public async Task Connect_VeNCryptX509Plain_SendsTheUserName()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		using X509Certificate2 certificate = TestCertificates.CreateServerCertificate();
		await using LoopbackVncServer server = VncHarness.StartServer(new LoopbackVncServerOptions
		{
			SecurityTypes = [(int)VncSecurityType.VeNCrypt],
			VeNCryptSubtypes = [(int)VncSecurityType.X509Plain],
			Certificate = certificate,
			Username = VncHarness.User,
			Password = VncHarness.Password,
		});

		await using IProtocolSession session = await VncHarness.CreateProvider().ConnectAsync(
			VncHarness.CreateContext(server.Port, new FakeCredentialSource(VncHarness.User, VncHarness.Password)),
			cancellationToken);
		LoopbackVncConnection accepted = await server.AcceptedAsync(cancellationToken);

		Assert.Equal(VncHarness.User, accepted.PlainUsername);
		Assert.True(accepted.PasswordAccepted);
	}

	[Fact(Timeout = VncHarness.TestTimeoutMilliseconds)]
	public async Task Connect_RejectedCertificate_FailsWithoutSendingThePassword()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		using X509Certificate2 certificate = TestCertificates.CreateServerCertificate();
		await using LoopbackVncServer server = VncHarness.StartServer(new LoopbackVncServerOptions
		{
			SecurityTypes = [(int)VncSecurityType.VeNCrypt],
			VeNCryptSubtypes = [(int)VncSecurityType.X509Vnc],
			Certificate = certificate,
			Password = VncHarness.Password,
		});

		FakeHostVerifier verifier = FakeHostVerifier.Rejecting();
		ProtocolConnectException failure = await Assert.ThrowsAsync<ProtocolConnectException>(
			() => VncHarness.CreateProvider().ConnectAsync(
				VncHarness.CreateContext(server.Port, new FakeCredentialSource(null, VncHarness.Password), verifier),
				cancellationToken));

		Assert.Equal(ConnectFailure.HostIdentityRejected, failure.Failure);
		Assert.Single(verifier.Seen);
	}

	[Fact(Timeout = VncHarness.TestTimeoutMilliseconds)]
	public async Task Connect_RequiredEncryptionWithoutVeNCrypt_Fails()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackVncServer server = VncHarness.StartServer(new LoopbackVncServerOptions
		{
			SecurityTypes = [(int)VncSecurityType.VncAuth],
			Password = VncHarness.Password,
		});

		ProtocolConnectException failure = await Assert.ThrowsAsync<ProtocolConnectException>(
			() => VncHarness.CreateProvider().ConnectAsync(
				VncHarness.CreateContext(
					server.Port,
					new FakeCredentialSource(null, VncHarness.Password),
					options: VncConnectionOptions.Default with { Encryption = VncEncryptionMode.Required }),
				cancellationToken));

		Assert.Equal(ConnectFailure.ProtocolError, failure.Failure);
		Assert.Contains("encrypted", failure.Message, StringComparison.Ordinal);
	}

	[Fact(Timeout = VncHarness.TestTimeoutMilliseconds)]
	public async Task Connect_ServerRefusesWithReason_ShowsIt()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackVncServer server = VncHarness.StartServer(new LoopbackVncServerOptions
		{
			RefuseReason = "Too many security failures",
		});

		ProtocolConnectException failure = await Assert.ThrowsAsync<ProtocolConnectException>(
			() => VncHarness.CreateProvider().ConnectAsync(VncHarness.CreateContext(server.Port), cancellationToken));

		Assert.Equal(ConnectFailure.ProtocolError, failure.Failure);
		Assert.Contains("Too many security failures", failure.Message, StringComparison.Ordinal);
	}

	[Fact(Timeout = VncHarness.TestTimeoutMilliseconds)]
	public async Task Connect_NotAVncServer_SaysSo()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackVncServer server = VncHarness.StartServer(new LoopbackVncServerOptions
		{
			Greeting = "SSH-2.0-Open",
			CloseAfterGreeting = true,
		});

		ProtocolConnectException failure = await Assert.ThrowsAsync<ProtocolConnectException>(
			() => VncHarness.CreateProvider().ConnectAsync(VncHarness.CreateContext(server.Port), cancellationToken));

		Assert.Equal(ConnectFailure.ProtocolError, failure.Failure);
		Assert.Contains("not answer like a VNC server", failure.Message, StringComparison.Ordinal);
	}

	[Fact(Timeout = VncHarness.TestTimeoutMilliseconds)]
	public async Task Connect_NothingListening_ReportsTheHostAsUnreachable()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		int port;
		await using (LoopbackVncServer server = VncHarness.StartServer())
		{
			port = server.Port;
		}

		ProtocolConnectException failure = await Assert.ThrowsAsync<ProtocolConnectException>(
			() => VncHarness.CreateProvider().ConnectAsync(VncHarness.CreateContext(port), cancellationToken));

		Assert.Equal(ConnectFailure.HostUnreachable, failure.Failure);
	}

	[Fact(Timeout = VncHarness.TestTimeoutMilliseconds)]
	public async Task Connect_PublicKeyLogin_IsRefusedBeforeDialing()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackVncServer server = VncHarness.StartServer();

		ProtocolConnectException failure = await Assert.ThrowsAsync<ProtocolConnectException>(
			() => VncHarness.CreateProvider().ConnectAsync(
				VncHarness.CreateContext(server.Port, new FakeCredentialSource(null, null, AuthenticationMethod.PublicKey)),
				cancellationToken));

		Assert.Equal(ConnectFailure.AuthenticationFailed, failure.Failure);
		Assert.Equal(0, server.Attempts);
	}
}
