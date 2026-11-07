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
/// The chain of steps an RDP connect reports for the session tab: which steps a sign-in route has, and where the
/// chain stands when a server stops at one of them. The servers are the fakes <see cref="RdpConnectStageTests"/> uses
/// for the messages.
/// </summary>
public sealed class RdpConnectStepsTests
{
	private static readonly TimeSpan Short = TimeSpan.FromSeconds(1.5);

	// Before its first message sspi-rs looks for a Kerberos server for the login's domain, which a slow resolver drags out.
	private static readonly TimeSpan LoginTime = TimeSpan.FromSeconds(5);

	private static readonly string[] NlaSteps =
	[
		RdpConnectSteps.Connect,
		RdpConnectSteps.Negotiate,
		RdpConnectSteps.Tls,
		RdpConnectSteps.Certificate,
		RdpConnectSteps.SignIn,
		RdpConnectSteps.Session,
	];

	[Fact]
	public void OnlyNetworkLevelAuthentication_HasASignInStepOfItsOwn()
	{
		Assert.Equal(NlaSteps, RdpConnectSteps.For(RdpSignInRoute.NetworkLevelAuthentication).Select(step => step.Id));
		Assert.Equal(
			NlaSteps.Where(id => id != RdpConnectSteps.SignIn),
			RdpConnectSteps.For(RdpSignInRoute.Automatic).Select(step => step.Id));
		Assert.Equal(
			NlaSteps.Where(id => id != RdpConnectSteps.SignIn),
			RdpConnectSteps.For(RdpSignInRoute.SignInScreen).Select(step => step.Id));
	}

	[Fact]
	public void EveryStage_ReportsAsAPlannedStep_AKerberosRequestAsTheSignIn()
	{
		Assert.Equal(RdpConnectSteps.Connect, RdpConnectSteps.Of(RdpConnectStage.Dialing));
		Assert.Equal(RdpConnectSteps.Negotiate, RdpConnectSteps.Of(RdpConnectStage.Negotiating));
		Assert.Equal(RdpConnectSteps.Tls, RdpConnectSteps.Of(RdpConnectStage.Tls));
		Assert.Equal(RdpConnectSteps.Certificate, RdpConnectSteps.Of(RdpConnectStage.Certificate));
		Assert.Equal(RdpConnectSteps.SignIn, RdpConnectSteps.Of(RdpConnectStage.Login));
		Assert.Equal(RdpConnectSteps.SignIn, RdpConnectSteps.Of(RdpConnectStage.Kerberos));
		Assert.Equal(RdpConnectSteps.Session, RdpConnectSteps.Of(RdpConnectStage.Session));
		Assert.All(Enum.GetValues<RdpConnectStage>(), stage => Assert.Contains(RdpConnectSteps.Of(stage), NlaSteps));
	}

	[Fact(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	public async Task Connect_ADialNobodyAnswers_StopsAtTheConnect()
	{
		RecordingConnectSteps steps = new();
		using LoginCredentials credentials = Login();
		RdpConnector connector = new("192.0.2.10", 3389, RdpConnectionOptions.Default, TimeSpan.FromSeconds(1), FakeHostVerifier.Accepting(), NeverAnswers)
		{
			Steps = steps,
		};

		ProtocolConnectException error = await Assert.ThrowsAsync<ProtocolConnectException>(
			() => connector.ConnectAsync(credentials, null, TestContext.Current.CancellationToken));

		Assert.Equal(NlaSteps, steps.Ids);
		Assert.Equal(RdpConnectSteps.Connect, steps.Chain.Current?.Step.Id);
		Assert.Equal(RdpConnectSteps.Connect, steps.EndedWith(error.Message).Failed?.Step.Id);
	}

	[Fact(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	public async Task Connect_AServerThatStallsInCredssp_IsAtTheSignIn()
	{
		await using FakeRdpServer server = new FakeRdpServer(FakeRdpServer.ProtocolHybrid).Start();
		RecordingConnectSteps steps = new();

		await ConnectFailsAsync(server, steps, LoginTime, TestContext.Current.CancellationToken);

		Assert.Equal(
			[
				ConnectStepState.Done,
				ConnectStepState.Done,
				ConnectStepState.Done,
				ConnectStepState.Done,
				ConnectStepState.Running,
				ConnectStepState.Pending,
			],
			States(steps.Chain));
	}

	[Fact(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	public async Task Connect_TlsWithoutCredssp_SkipsTheSignInStep()
	{
		await using FakeRdpServer server = new FakeRdpServer(FakeRdpServer.ProtocolSsl).Start();
		RecordingConnectSteps steps = new();

		await ConnectFailsAsync(server, steps, Short, TestContext.Current.CancellationToken);

		Assert.Equal(ConnectStepState.Skipped, steps.StateOf(RdpConnectSteps.SignIn));
		Assert.Equal(RdpConnectSteps.Session, steps.Chain.Current?.Step.Id);
	}

	[Fact(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	public async Task Connect_ARejectedCertificate_BreaksAtTheCertificate()
	{
		await using FakeRdpServer server = new FakeRdpServer(FakeRdpServer.ProtocolHybrid).Start();
		RecordingConnectSteps steps = new();

		ProtocolConnectException error = await ConnectFailsAsync(server, steps, Short, TestContext.Current.CancellationToken, FakeHostVerifier.Rejecting());

		ConnectStepChain ended = steps.EndedWith(error.Message);
		Assert.Equal(RdpConnectSteps.Certificate, ended.Failed?.Step.Id);
		Assert.Equal(error.Message, ended.Failed?.Detail);
		Assert.All(States(ended).TakeLast(2), state => Assert.Equal(ConnectStepState.Pending, state));
	}

	[Fact(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	public async Task Connect_WithoutNla_PlansNoSignInStep()
	{
		await using FakeRdpServer server = new FakeRdpServer(FakeRdpServer.ProtocolSsl).Start();
		RecordingConnectSteps steps = new();
		using LoginCredentials credentials = Login();
		RdpConnector connector = new(
			"127.0.0.1",
			server.Port,
			RdpConnectionOptions.Default with { NetworkLevelAuthentication = false },
			Short,
			FakeHostVerifier.Accepting())
		{
			Steps = steps,
		};

		await Assert.ThrowsAsync<ProtocolConnectException>(() => connector.ConnectAsync(credentials, null, TestContext.Current.CancellationToken));

		Assert.DoesNotContain(RdpConnectSteps.SignIn, steps.Ids);
		Assert.Equal(RdpConnectSteps.Session, steps.Chain.Current?.Step.Id);
	}

	[Fact(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	public async Task Connect_ANameCredsspCannotCarry_FailsTheSignInBeforeDialing()
	{
		await using FakeRdpServer server = new FakeRdpServer(FakeRdpServer.ProtocolHybrid).Start();
		RecordingConnectSteps steps = new();
		using LoginCredentials credentials = Login("MicrosoftAccount\\you@example.com");
		RdpConnector connector = new("127.0.0.1", server.Port, RdpConnectionOptions.Default, Short, FakeHostVerifier.Accepting())
		{
			Steps = steps,
		};

		RdpAuthenticationException error = await Assert.ThrowsAsync<RdpAuthenticationException>(
			() => connector.ConnectAsync(credentials, null, TestContext.Current.CancellationToken));

		Assert.Equal(0, server.Accepted);
		Assert.Equal(RdpConnectSteps.SignIn, steps.Chain.Failed?.Step.Id);
		Assert.Equal(error.Message, steps.Chain.Failed?.Detail);
		Assert.Equal(ConnectStepState.Pending, steps.StateOf(RdpConnectSteps.Connect));
	}

	[Fact(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	public async Task Provider_ARefusedLogin_StaysOnTheSignInStep()
	{
		await using FakeRdpServer server = new FakeRdpServer(FakeRdpServer.ProtocolHybrid)
		{
			Then = static _ => FakeRdpServer.AfterTls.RefuseLogin,
		}.Start();
		FakeSettingsService settings = new();
		settings.Set(new RdpSettings { ConnectTimeoutSeconds = 10, AuthenticationAttempts = 2 });
		FakeCredentialSource credentials = new(RdpHarness.User, "first", AuthenticationMethod.Password, "second");
		RecordingConnectSteps steps = new();
		ProtocolConnectContext context = RdpHarness.CreateContext(server.Port, credentials, steps: steps);

		ProtocolConnectException error = await Assert.ThrowsAsync<ProtocolConnectException>(
			() => RdpHarness.CreateProvider(settings).ConnectAsync(context, TestContext.Current.CancellationToken));

		// Planned again for the second password, and refused again there.
		Assert.Equal(2, steps.Plans);
		ConnectStepStatus failed = Assert.IsType<ConnectStepStatus>(steps.Chain.Failed);
		Assert.Equal(RdpConnectSteps.SignIn, failed.Step.Id);
		Assert.Equal(error.Message, failed.Detail);
		Assert.Equal(ConnectStepState.Done, steps.StateOf(RdpConnectSteps.Certificate));
	}

	private static async Task<ProtocolConnectException> ConnectFailsAsync(
		FakeRdpServer server,
		RecordingConnectSteps steps,
		TimeSpan timeout,
		CancellationToken cancellationToken,
		IHostIdentityVerifier? verifier = null)
	{
		using LoginCredentials credentials = Login();
		RdpConnector connector = new("127.0.0.1", server.Port, RdpConnectionOptions.Default, timeout, verifier ?? FakeHostVerifier.Accepting())
		{
			Steps = steps,
		};

		return await Assert.ThrowsAsync<ProtocolConnectException>(
			() => connector.ConnectAsync(credentials, null, cancellationToken));
	}

	private static async Task<Socket> NeverAnswers(string host, int port, TimeSpan budget, Action<IPAddress>? tried, CancellationToken cancellationToken)
	{
		await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
		throw new InvalidOperationException("A delay without end ended.");
	}

	private static LoginCredentials Login(string username = RdpHarness.User) => new()
	{
		Username = username,
		Method = AuthenticationMethod.Password,
		Password = SecretBuffer.FromString(RdpHarness.Password),
	};

	private static ConnectStepState[] States(ConnectStepChain chain) => [.. chain.Steps.Select(status => status.State)];
}
