using System.Net;
using System.Net.Sockets;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;
using Mokaterm.Modules.Rdp.Protocol;
using Mokaterm.Modules.Rdp.Tests.Fakes;

namespace Mokaterm.Modules.Rdp.Tests;

/// <summary>
/// The connect sequence against a server that stops at one step: what the user reads when it times out or fails there,
/// the status lines on the way, and that the connection is closed afterwards. Everything up to and including the TLS
/// handshake is real; past it the server only reads, which is enough to see CredSSP start and the session begin.
/// </summary>
public sealed class RdpConnectStageTests
{
	private static readonly TimeSpan Short = TimeSpan.FromSeconds(1.5);

	private static readonly TimeSpan CloseWait = TimeSpan.FromSeconds(10);

	// Before its first message sspi-rs looks for a Kerberos server for the login's domain with a DNS query, so a test
	// that waits for that message gives the query room on a slow resolver.
	private static readonly TimeSpan LoginTime = TimeSpan.FromSeconds(5);

	[Fact(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	public async Task Connect_ADialNobodyAnswers_SaysWhatHasToBeOnAtThatPc()
	{
		RecordingProgress<string> status = new();
		RdpConnector connector = new("192.0.2.10", 3389, RdpConnectionOptions.Default, TimeSpan.FromSeconds(1), FakeHostVerifier.Accepting(), NeverAnswers);

		ProtocolConnectException error = await Assert.ThrowsAsync<ProtocolConnectException>(
			() => connector.ConnectAsync(Login(), status, TestContext.Current.CancellationToken));

		Assert.Equal(ConnectFailure.Timeout, error.Failure);
		Assert.StartsWith("192.0.2.10:3389 did not answer in time. Remote Desktop has to be turned on", error.Message, StringComparison.Ordinal);
		Assert.Equal<string>(["Connecting to 192.0.2.10:3389"], [.. status.Reports]);
	}

	[Fact(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	public async Task Connect_AServerThatTakesTheConnectionAndSaysNothing_TimesOutAtTheRdpRequest()
	{
		await using FakeRdpServer server = new FakeRdpServer(selectedProtocol: null).Start();

		ProtocolConnectException error = await ConnectFailsAsync(server, TestContext.Current.CancellationToken);

		Assert.Equal(ConnectFailure.Timeout, error.Failure);
		Assert.Contains("did not answer the RDP request in time", error.Message, StringComparison.Ordinal);
		await server.ClientClosed.WaitAsync(CloseWait, TestContext.Current.CancellationToken);
	}

	[Fact(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	public async Task Connect_AServerThatNeverStartsTls_TimesOutInTheHandshake()
	{
		await using FakeRdpServer server = new FakeRdpServer(FakeRdpServer.ProtocolSsl, startTls: false).Start();

		ProtocolConnectException error = await ConnectFailsAsync(server, TestContext.Current.CancellationToken);

		Assert.Equal(ConnectFailure.Timeout, error.Failure);
		Assert.Equal($"127.0.0.1:{server.Port} did not finish the TLS handshake in time.", error.Message);
		await server.ClientClosed.WaitAsync(CloseWait, TestContext.Current.CancellationToken);
	}

	// The certificate used to be checked after CredSSP, when the password had already gone to whoever answered.
	[Fact(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	public async Task Connect_ARejectedCertificate_SendsNoLogin()
	{
		await using FakeRdpServer server = new FakeRdpServer(FakeRdpServer.ProtocolHybrid).Start();
		FakeHostVerifier verifier = FakeHostVerifier.Rejecting();

		ProtocolConnectException error = await ConnectFailsAsync(server, TestContext.Current.CancellationToken, verifier: verifier);

		Assert.Equal(ConnectFailure.HostIdentityRejected, error.Failure);
		Assert.Single(verifier.Seen);
		await server.ClientClosed.WaitAsync(CloseWait, TestContext.Current.CancellationToken);
		Assert.Equal(0, server.BytesAfterTls);
	}

	[Fact(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	public async Task Connect_AServerThatStallsInCredssp_SaysTheLoginCheckDidNotFinish()
	{
		await using FakeRdpServer server = new FakeRdpServer(FakeRdpServer.ProtocolHybrid).Start();
		RecordingProgress<string> status = new();

		ProtocolConnectException error = await ConnectFailsAsync(server, TestContext.Current.CancellationToken, status: status, timeout: LoginTime);

		Assert.Equal(ConnectFailure.Timeout, error.Failure);
		Assert.Equal($"127.0.0.1:{server.Port} did not finish checking the login (CredSSP) in time.", error.Message);
		Assert.Equal<string>(
			[
				$"Connecting to 127.0.0.1:{server.Port}",
				"Negotiating security",
				"Starting TLS",
				"Checking the certificate",
				"Checking the login (CredSSP)",
			],
			[.. status.Reports]);

		// The first CredSSP message, NTLM's negotiate, went out, and the connection was closed once the attempt ended.
		await server.ClientClosed.WaitAsync(CloseWait, TestContext.Current.CancellationToken);
		Assert.True(server.BytesAfterTls > 0);
	}

	[Fact(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	public async Task Connect_AServerThatHangsUpBeforeTheLoginWentOut_IsNotTakenForAWrongPassword()
	{
		await using FakeRdpServer server = new FakeRdpServer(FakeRdpServer.ProtocolHybrid)
		{
			Then = static _ => FakeRdpServer.AfterTls.ReadOnceThenHangUp,
		}.Start();

		ProtocolConnectException error = await ConnectFailsAsync(server, TestContext.Current.CancellationToken, timeout: TimeSpan.FromSeconds(10));

		Assert.Equal(ConnectFailure.ProtocolError, error.Failure);
		Assert.Contains("while checking the login", error.Message, StringComparison.Ordinal);
	}

	// What sspi-rs's NTLM puts in the authenticate message for each way of writing a login, read by a fake server that
	// then turns the login down the way Windows does for a wrong password.
	[Theory(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	[InlineData("alice", "", "", "alice")]
	[InlineData("CORP\\alice", "", "CORP", "alice")]
	[InlineData("alice", "CORP", "CORP", "alice")]
	[InlineData("CORP\\alice", "OTHER", "CORP", "alice")]
	[InlineData(".\\alice", "", ".", "alice")]
	[InlineData("alice@corp.example", "", "corp.example", "alice")]
	public async Task Connect_ARefusedLogin_CarriesTheNameAsNtlmSplitsIt(string username, string domain, string sentDomain, string sentUser)
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using FakeRdpServer server = new FakeRdpServer(FakeRdpServer.ProtocolHybrid)
		{
			Then = static _ => FakeRdpServer.AfterTls.RefuseLogin,
		}.Start();
		using LoginCredentials credentials = Login(username);
		RdpConnector connector = new(
			"127.0.0.1",
			server.Port,
			RdpConnectionOptions.Default with { Domain = domain },
			LoginTime,
			FakeHostVerifier.Accepting());

		RdpAuthenticationException error = await Assert.ThrowsAsync<RdpAuthenticationException>(
			() => connector.ConnectAsync(credentials, null, cancellationToken));

		Assert.Equal((sentDomain, sentUser), Assert.Single(server.Logins));
		Assert.StartsWith($"127.0.0.1:{server.Port} refused the login:", error.Message, StringComparison.Ordinal);
		Assert.Contains("STATUS_LOGON_FAILURE", error.Message, StringComparison.Ordinal);
	}

	// sspi-rs has no form for MicrosoftAccount\you@example.com, and finding that out used to take a whole connect.
	[Theory(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	[InlineData("MicrosoftAccount\\you@example.com", "")]
	[InlineData("you@example.com", "MicrosoftAccount")]
	public async Task Connect_AMicrosoftAccountName_IsRefusedBeforeDialingWithTheNameThatWorks(string username, string domain)
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using FakeRdpServer server = new FakeRdpServer(FakeRdpServer.ProtocolHybrid).Start();
		using LoginCredentials credentials = Login(username);
		RdpConnector connector = new(
			"127.0.0.1",
			server.Port,
			RdpConnectionOptions.Default with { Domain = domain },
			Short,
			FakeHostVerifier.Accepting());

		RdpAuthenticationException error = await Assert.ThrowsAsync<RdpAuthenticationException>(
			() => connector.ConnectAsync(credentials, null, cancellationToken));

		Assert.Contains("run whoami", error.Message, StringComparison.Ordinal);
		Assert.Equal(0, server.Accepted);
	}

	// An Entra ID account cannot pass CredSSP from here in any form, and no other name typed into a password prompt
	// would change that, so it is refused before dialing without asking again.
	[Theory(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	[InlineData("AzureAD\\you@company.com", "")]
	[InlineData("you@company.com", "AzureAD")]
	[InlineData("AzureAD\\you", "")]
	public async Task Connect_AnEntraIdAccountWithNla_IsRefusedBeforeDialingWithWhatWorks(string username, string domain)
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using FakeRdpServer server = new FakeRdpServer(FakeRdpServer.ProtocolHybrid).Start();
		using LoginCredentials credentials = Login(username);
		RdpConnector connector = new(
			"127.0.0.1",
			server.Port,
			RdpConnectionOptions.Default with { Domain = domain },
			Short,
			FakeHostVerifier.Accepting());

		ProtocolConnectException error = await Assert.ThrowsAsync<ProtocolConnectException>(
			() => connector.ConnectAsync(credentials, null, cancellationToken));

		Assert.Equal(ConnectFailure.AuthenticationFailed, error.Failure);
		Assert.Contains("Require devices to use Network Level Authentication to connect", error.Message, StringComparison.Ordinal);
		Assert.Equal(0, server.Accepted);
	}

	// Without network level authentication the name goes to Windows as written, in the Client Info PDU, so nothing here
	// refuses it (RdpSignInTests reads what it carries).
	[Fact(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	public async Task Connect_AMicrosoftAccountNameWithoutCredssp_IsLeftToTheServer()
	{
		await using FakeRdpServer server = new FakeRdpServer(FakeRdpServer.ProtocolSsl).Start();
		using LoginCredentials credentials = Login("MicrosoftAccount\\you@example.com");
		RdpConnector connector = new(
			"127.0.0.1",
			server.Port,
			RdpConnectionOptions.Default with { NetworkLevelAuthentication = false },
			Short,
			FakeHostVerifier.Accepting());

		ProtocolConnectException error = await Assert.ThrowsAsync<ProtocolConnectException>(
			() => connector.ConnectAsync(credentials, null, TestContext.Current.CancellationToken));

		Assert.Contains("setting up the session", error.Message, StringComparison.Ordinal);
	}

	[Fact(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	public async Task Connect_ARefusedLogin_AsksForAnotherPasswordAndDialsAgain()
	{
		await using FakeRdpServer server = new FakeRdpServer(FakeRdpServer.ProtocolHybrid)
		{
			Then = static _ => FakeRdpServer.AfterTls.RefuseLogin,
		}.Start();
		FakeSettingsService settings = new();
		settings.Set(new RdpSettings { ConnectTimeoutSeconds = 10, AuthenticationAttempts = 2 });
		FakeCredentialSource credentials = new(RdpHarness.User, "first", AuthenticationMethod.Password, "second");
		ProtocolConnectContext context = RdpHarness.CreateContext(server.Port, credentials);

		ProtocolConnectException error = await Assert.ThrowsAsync<ProtocolConnectException>(
			() => RdpHarness.CreateProvider(settings).ConnectAsync(context, TestContext.Current.CancellationToken));

		Assert.Equal(ConnectFailure.AuthenticationFailed, error.Failure);
		Assert.Equal(2, server.Accepted);
		Assert.Equal(2, server.Logins.Count);
		Assert.Single(credentials.RetryReasons);
		Assert.Contains("STATUS_LOGON_FAILURE", Assert.Single(credentials.RetryReasons), StringComparison.Ordinal);
	}

	[Fact(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	public async Task Connect_TlsWithoutCredssp_TimesOutSettingUpTheSessionAndNamesTheStep()
	{
		await using FakeRdpServer server = new FakeRdpServer(FakeRdpServer.ProtocolSsl).Start();
		RecordingProgress<string> status = new();

		ProtocolConnectException error = await ConnectFailsAsync(server, TestContext.Current.CancellationToken, status: status);

		Assert.Equal(ConnectFailure.Timeout, error.Failure);
		Assert.Equal(
			$"127.0.0.1:{server.Port} did not finish setting up the session in time. It stopped answering at BasicSettingsExchangeWaitResponse.",
			error.Message);
		Assert.Equal<string>(
			[
				$"Connecting to 127.0.0.1:{server.Port}",
				"Negotiating security",
				"Starting TLS",
				"Checking the certificate",
				"Setting up the session",
			],
			[.. status.Reports]);
		await server.ClientClosed.WaitAsync(CloseWait, TestContext.Current.CancellationToken);
	}

	[Fact(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	public async Task Connect_ACertificatePrompt_DoesNotCountAgainstTheDeadline()
	{
		await using FakeRdpServer server = new FakeRdpServer(FakeRdpServer.ProtocolHybrid).Start();
		FakeHostVerifier verifier = FakeHostVerifier.AnsweringAfter(TimeSpan.FromSeconds(2.5), true);

		ProtocolConnectException error = await ConnectFailsAsync(server, TestContext.Current.CancellationToken, verifier: verifier);

		// The prompt outlasted the whole deadline, and the attempt still went on to the login.
		Assert.Contains("checking the login (CredSSP)", error.Message, StringComparison.Ordinal);
	}

	[Fact(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	public async Task Connect_AServerThatGaveUpDuringThePrompt_IsDialedAgain()
	{
		await using FakeRdpServer server = new FakeRdpServer(FakeRdpServer.ProtocolHybrid)
		{
			Then = static number => number == 1 ? FakeRdpServer.AfterTls.HangUp : FakeRdpServer.AfterTls.Hold,
		}.Start();
		int asked = 0;
		FakeHostVerifier verifier = new()
		{
			// The first answer takes as long as a user reading a fingerprint; the second comes from what they said.
			Decide = async (_, cancellationToken) =>
			{
				if (Interlocked.Increment(ref asked) == 1)
				{
					await Task.Delay(RdpConnectSequence.PromptThreshold + TimeSpan.FromSeconds(0.5), cancellationToken);
				}

				return true;
			},
		};

		ProtocolConnectException error = await ConnectFailsAsync(server, TestContext.Current.CancellationToken, verifier: verifier);

		Assert.Equal(2, server.Accepted);
		Assert.Equal(2, verifier.Seen.Count);
		Assert.Contains("checking the login (CredSSP)", error.Message, StringComparison.Ordinal);
	}

	[Fact(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	public async Task Connect_CancelledByTheUser_ClosesTheConnection()
	{
		await using FakeRdpServer server = new FakeRdpServer(FakeRdpServer.ProtocolHybrid).Start();
		using CancellationTokenSource user = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
		CancellingProgress status = new("Checking the login (CredSSP)", user);
		RdpConnector connector = Connector(server.Port, TimeSpan.FromSeconds(30));

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connector.ConnectAsync(Login(), status, user.Token));

		await server.ClientClosed.WaitAsync(CloseWait, TestContext.Current.CancellationToken);
	}

	private static async Task<Socket> NeverAnswers(string host, int port, TimeSpan budget, Action<IPAddress>? tried, CancellationToken cancellationToken)
	{
		await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
		throw new InvalidOperationException("A delay without end ended.");
	}

	private static RdpConnector Connector(int port, TimeSpan timeout, IHostIdentityVerifier? verifier = null) =>
		new("127.0.0.1", port, RdpConnectionOptions.Default, timeout, verifier ?? FakeHostVerifier.Accepting());

	private static LoginCredentials Login(string username = RdpHarness.User) => new()
	{
		Username = username,
		Method = AuthenticationMethod.Password,
		Password = SecretBuffer.FromString(RdpHarness.Password),
	};

	private static async Task<ProtocolConnectException> ConnectFailsAsync(
		FakeRdpServer server,
		CancellationToken cancellationToken,
		IHostIdentityVerifier? verifier = null,
		IProgress<string>? status = null,
		LoginCredentials? login = null,
		TimeSpan? timeout = null)
	{
		using LoginCredentials credentials = login ?? Login();
		RdpConnector connector = Connector(server.Port, timeout ?? Short, verifier);
		return await Assert.ThrowsAsync<ProtocolConnectException>(
			() => connector.ConnectAsync(credentials, status, cancellationToken));
	}

	/// <summary>Cancels the attempt as soon as it reports <paramref name="status"/>, as a user closing the tab would.</summary>
	private sealed class CancellingProgress(string status, CancellationTokenSource source) : IProgress<string>
	{
		public void Report(string value)
		{
			if (value == status)
			{
				source.Cancel();
			}
		}
	}
}
