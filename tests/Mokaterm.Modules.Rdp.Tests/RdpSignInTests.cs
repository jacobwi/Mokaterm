using System.Collections.Concurrent;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;
using Mokaterm.Modules.Rdp.Protocol;
using Mokaterm.Modules.Rdp.Tests.Fakes;

namespace Mokaterm.Modules.Rdp.Tests;

/// <summary>
/// What goes to the server on each sign-in route, read on the wire by a loopback server: the protocols the X.224
/// request asks for, and the Client Info PDU, which a fake server reaches by playing the MCS steps before it. That PDU
/// carries the password whenever the login signs in by itself, so these are the tests that it goes out only inside TLS,
/// only after the certificate was accepted, and never to a server that picked Standard RDP Security.
/// </summary>
public sealed class RdpSignInTests
{
	private const uint Ssl = (uint)RdpSecurityProtocols.Ssl;

	private static readonly TimeSpan Short = TimeSpan.FromSeconds(1.5);

	private static readonly TimeSpan SessionTime = TimeSpan.FromSeconds(10);

	private static readonly TimeSpan CloseWait = TimeSpan.FromSeconds(10);

	private static readonly RdpConnectionOptions NlaOff = RdpConnectionOptions.Default with { NetworkLevelAuthentication = false };

	[Fact(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	public async Task NlaOff_AsksForTlsAloneAndSignsInThroughTheClientInfoAfterTheCertificate()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		ConcurrentQueue<string> events = new();
		await using FakeRdpServer server = new FakeRdpServer(FakeRdpServer.ProtocolSsl)
		{
			Then = static _ => FakeRdpServer.AfterTls.PlaySession,
			Events = events,
		}.Start();
		FakeHostVerifier verifier = new()
		{
			Decide = (_, _) =>
			{
				events.Enqueue("certificate");
				return Task.FromResult(true);
			},
		};
		using LoginCredentials credentials = Login(RdpHarness.User);

		await Assert.ThrowsAsync<ProtocolConnectException>(
			() => new RdpConnector("127.0.0.1", server.Port, NlaOff, SessionTime, verifier).ConnectAsync(credentials, null, cancellationToken));

		Assert.Equal(Ssl, Assert.Single(server.RequestedProtocols));
		FakeClientInfo info = Assert.Single(server.ClientInfos);
		Assert.Equal(("", RdpHarness.User, RdpHarness.Password), (info.Domain, info.User, info.Password));
		Assert.True(info.SignsInAutomatically);
		Assert.True(
			events.ToList().IndexOf("certificate") < events.ToList().IndexOf("client info"),
			"The certificate was checked before the Client Info PDU arrived: " + string.Join(", ", events));
	}

	// IronRDP takes one domain and one user name for both CredSSP and the Client Info PDU, which carries them as plain
	// strings: these are the forms Microsoft documents for Entra ID with network level authentication off, and the
	// split FreeRDP makes.
	[Theory(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	[InlineData("AzureAD\\you@company.com", "", "AzureAD", "you@company.com")]
	[InlineData("you@company.com", "AzureAD", "AzureAD", "you@company.com")]
	[InlineData("MicrosoftAccount\\you@outlook.com", "", "MicrosoftAccount", "you@outlook.com")]
	[InlineData("CORP\\alice", "", "CORP", "alice")]
	[InlineData("CORP\\alice", "OTHER", "CORP", "alice")]
	[InlineData("alice@corp.example", "", "", "alice@corp.example")]
	[InlineData(".\\alice", "", ".", "alice")]
	[InlineData("alice", "", "", "alice")]
	[InlineData("alice", "CORP", "CORP", "alice")]
	public async Task NlaOff_TheClientInfoCarriesTheNameAsWritten(string username, string domain, string sentDomain, string sentUser)
	{
		FakeClientInfo info = await SignInAsync(NlaOff with { Domain = domain }, Login(username), TestContext.Current.CancellationToken);

		Assert.Equal((sentDomain, sentUser), (info.Domain, info.User));
		Assert.Equal(RdpHarness.Password, info.Password);
		Assert.True(info.SignsInAutomatically);
	}

	[Fact(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	public async Task NlaOffWithAutologonOff_SendsTheNameButNoPasswordAndLeavesTheSignInScreenUp()
	{
		FakeClientInfo info = await SignInAsync(NlaOff with { Autologon = false }, Login("CORP\\alice"), TestContext.Current.CancellationToken);

		Assert.Equal(("CORP", "alice", ""), (info.Domain, info.User, info.Password));
		Assert.False(info.SignsInAutomatically);
	}

	[Fact(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	public async Task NlaOffWithNoPasswordSaved_SendsNoneAndLeavesTheSignInScreenUp()
	{
		using LoginCredentials credentials = new() { Username = RdpHarness.User, Method = AuthenticationMethod.Password };

		FakeClientInfo info = await SignInAsync(NlaOff, credentials, TestContext.Current.CancellationToken);

		Assert.Equal((RdpHarness.User, ""), (info.User, info.Password));
		Assert.False(info.SignsInAutomatically);
	}

	[Fact(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	public async Task AnAnonymousLogin_AsksForTlsAloneAndSendsNoPassword()
	{
		using LoginCredentials credentials = new() { Username = "", Method = AuthenticationMethod.Anonymous };

		FakeClientInfo info = await SignInAsync(RdpConnectionOptions.Default, credentials, TestContext.Current.CancellationToken, expectRequest: Ssl);

		Assert.Equal(("", ""), (info.User, info.Password));
		Assert.False(info.SignsInAutomatically);
	}

	// A server that answers a request for CredSSP with TLS alone gets the login the way an automatic sign-in sends it,
	// which is what IronRDP does with a CredSSP config; it still waits for the certificate.
	[Fact(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	public async Task NlaOnAndAServerThatPicksTlsAlone_SendsTheLoginInTheClientInfo()
	{
		FakeClientInfo info = await SignInAsync(
			RdpConnectionOptions.Default,
			Login(RdpHarness.User),
			TestContext.Current.CancellationToken,
			expectRequest: (uint)(RdpSecurityProtocols.Ssl | RdpSecurityProtocols.Hybrid | RdpSecurityProtocols.HybridEx));

		Assert.Equal((RdpHarness.User, RdpHarness.Password), (info.User, info.Password));
		Assert.True(info.SignsInAutomatically);
	}

	[Fact(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	public async Task NlaOffAndARejectedCertificate_SendsNothingAfterTheHandshake()
	{
		await using FakeRdpServer server = new FakeRdpServer(FakeRdpServer.ProtocolSsl).Start();
		using LoginCredentials credentials = Login(RdpHarness.User);

		ProtocolConnectException error = await Assert.ThrowsAsync<ProtocolConnectException>(
			() => new RdpConnector("127.0.0.1", server.Port, NlaOff, Short, FakeHostVerifier.Rejecting())
				.ConnectAsync(credentials, null, TestContext.Current.CancellationToken));

		Assert.Equal(ConnectFailure.HostIdentityRejected, error.Failure);
		await server.ClientClosed.WaitAsync(CloseWait, TestContext.Current.CancellationToken);
		Assert.Equal(0, server.BytesAfterTls);
	}

	// Standard RDP Security would carry the Client Info PDU under RC4 with nothing proving the server. IronRDP never asks
	// for it; a server that picks it anyway, or that answers with no negotiation at all as servers before RDP 5.2 do, is
	// left before the TLS handshake would have started.
	[Theory(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	[InlineData(true, true)]
	[InlineData(true, false)]
	[InlineData(false, true)]
	public async Task AServerThatPicksStandardRdpSecurity_NeverGetsTheLogin(bool nla, bool negotiates)
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using FakeRdpServer server = new FakeRdpServer(FakeRdpServer.ProtocolRdp)
		{
			Confirm = negotiates ? null : FakeRdpServer.ConfirmWithoutNegotiation(),
			Then = static _ => FakeRdpServer.AfterTls.PlaySession,
		}.Start();
		using LoginCredentials credentials = Login(RdpHarness.User);

		ProtocolConnectException error = await Assert.ThrowsAsync<ProtocolConnectException>(
			() => new RdpConnector("127.0.0.1", server.Port, RdpConnectionOptions.Default with { NetworkLevelAuthentication = nla }, Short, FakeHostVerifier.Accepting())
				.ConnectAsync(credentials, null, cancellationToken));

		Assert.Equal(ConnectFailure.ProtocolError, error.Failure);
		Assert.Equal(
			$"127.0.0.1:{server.Port} offered only Standard RDP Security, whose RC4 encryption proves nothing about the server, so this " +
			"client sent nothing more. On that PC, set \"Require use of specific security layer for remote (RDP) connections\" to " +
			"Negotiate or SSL.",
			error.Message);
		await server.ClientClosed.WaitAsync(CloseWait, cancellationToken);
		Assert.Equal(0, server.TlsHandshakes);
		Assert.Empty(server.ClientInfos);
	}

	// IronRDP only checks that the pick shares a bit with the request; CredSSP picked for a request of TLS alone would
	// run a login nobody configured.
	[Fact(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	public async Task AServerThatPicksAProtocolNobodyAskedFor_IsLeftBeforeTls()
	{
		await using FakeRdpServer server = new FakeRdpServer(FakeRdpServer.ProtocolSsl | FakeRdpServer.ProtocolHybrid).Start();
		using LoginCredentials credentials = Login(RdpHarness.User);

		ProtocolConnectException error = await Assert.ThrowsAsync<ProtocolConnectException>(
			() => new RdpConnector("127.0.0.1", server.Port, NlaOff, Short, FakeHostVerifier.Accepting())
				.ConnectAsync(credentials, null, TestContext.Current.CancellationToken));

		Assert.Equal(
			$"127.0.0.1:{server.Port} picked protocols 0x00000003, which this client did not ask for, so it sent nothing more.",
			error.Message);
		await server.ClientClosed.WaitAsync(CloseWait, TestContext.Current.CancellationToken);
		Assert.Equal(0, server.TlsHandshakes);
	}

	[Fact(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	public async Task AServerThatRequiresNlaWhileItIsOff_SaysSoAndHowToTurnItBackOn()
	{
		await using FakeRdpServer server = new FakeRdpServer(FakeRdpServer.ProtocolSsl)
		{
			Confirm = FakeRdpServer.NegotiationFailure((int)RdpNegotiation.HybridRequiredByServer),
		}.Start();
		using LoginCredentials credentials = Login(RdpHarness.User);
		RdpConnector connector = new("127.0.0.1", server.Port, NlaOff, Short, FakeHostVerifier.Accepting());

		ProtocolConnectException error = await Assert.ThrowsAsync<ProtocolConnectException>(
			() => connector.ConnectAsync(credentials, null, TestContext.Current.CancellationToken));

		Assert.Equal(ConnectFailure.ProtocolError, error.Failure);
		Assert.StartsWith(
			$"127.0.0.1:{server.Port} requires network level authentication (HYBRID_REQUIRED_BY_SERVER), which is off for this login. " +
			"Turn \"Network level authentication\" back on in this login's RDP options",
			error.Message,
			StringComparison.Ordinal);
		Assert.Null(connector.LastNegotiated);
		Assert.Equal(RdpSignInRoute.Automatic, connector.LastRoute);
	}

	[Fact(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	public async Task TheConnector_RemembersWhatTheServerPicked()
	{
		await using FakeRdpServer server = new FakeRdpServer(FakeRdpServer.ProtocolSsl).Start();
		using LoginCredentials credentials = Login(RdpHarness.User);
		RdpConnector connector = new("127.0.0.1", server.Port, NlaOff, Short, FakeHostVerifier.Accepting());

		await Assert.ThrowsAsync<ProtocolConnectException>(() => connector.ConnectAsync(credentials, null, TestContext.Current.CancellationToken));

		Assert.Equal(RdpSecurityProtocols.Ssl, connector.LastNegotiated);
	}

	private static async Task<FakeClientInfo> SignInAsync(
		RdpConnectionOptions options,
		LoginCredentials credentials,
		CancellationToken cancellationToken,
		uint expectRequest = Ssl)
	{
		await using FakeRdpServer server = new FakeRdpServer(FakeRdpServer.ProtocolSsl)
		{
			Then = static _ => FakeRdpServer.AfterTls.PlaySession,
		}.Start();

		// The fake hangs up once it has the Client Info PDU, which ends the attempt in the session stage.
		ProtocolConnectException error = await Assert.ThrowsAsync<ProtocolConnectException>(
			() => new RdpConnector("127.0.0.1", server.Port, options, SessionTime, FakeHostVerifier.Accepting()).ConnectAsync(credentials, null, cancellationToken));

		Assert.Contains("setting up the session", error.Message, StringComparison.Ordinal);
		Assert.Equal(expectRequest, Assert.Single(server.RequestedProtocols));
		return Assert.Single(server.ClientInfos);
	}

	private static LoginCredentials Login(string username) => new()
	{
		Username = username,
		Method = AuthenticationMethod.Password,
		Password = SecretBuffer.FromString(RdpHarness.Password),
	};
}
