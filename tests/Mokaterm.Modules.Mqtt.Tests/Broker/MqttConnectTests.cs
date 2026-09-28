using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Mqtt.Tests.Fakes;

namespace Mokaterm.Modules.Mqtt.Tests.Broker;

/// <summary>What a connect does against a real broker on a loopback port.</summary>
public sealed class MqttConnectTests
{
	[Fact(Timeout = MqttHarness.TestTimeoutMilliseconds)]
	public async Task Connect_ToABrokerWithOpenAccess_OpensASessionWithNoLogin()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackBroker broker = await LoopbackBroker.StartAsync();
		RecordingProgress<string> status = new();

		(IProtocolSession session, IMqttConnection connection) = await MqttHarness.ConnectAsync(
			MqttHarness.CreateProvider(),
			MqttHarness.CreateContext(broker.Port, status: status),
			cancellationToken);

		await using (session)
		{
			Assert.Equal(MqttConnectionState.Connected, connection.State);
			Assert.Null(connection.CloseReason);
			Assert.False(connection.Info.IsEncrypted);
			Assert.Equal(MqttTransport.Tcp, connection.Info.Transport);
			Assert.Equal(MqttProtocolLevel.V500, connection.Info.Protocol);
			Assert.Equal($"127.0.0.1:{broker.Port}", connection.Info.Endpoint);
			Assert.False(connection.Info.SessionPresent);
			Assert.Contains("Connecting", status.Reports);
			Assert.Contains("Subscribing", status.Reports);

			// A session with nothing saved sends no user name at all, which is a different CONNECT to an empty one.
			(string ClientId, string? UserName) connect = Assert.Single(broker.Connects);
			Assert.Null(connect.UserName);
			Assert.StartsWith("mokaterm-", connect.ClientId, StringComparison.Ordinal);
			Assert.Equal(connect.ClientId, connection.Info.ClientId);
		}
	}

	[Fact(Timeout = MqttHarness.TestTimeoutMilliseconds)]
	public async Task Connect_WithTheConnectionsOwnClientId_SendsThatOne()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackBroker broker = await LoopbackBroker.StartAsync();

		(IProtocolSession session, IMqttConnection connection) = await MqttHarness.ConnectAsync(
			MqttHarness.CreateProvider(),
			MqttHarness.CreateContext(broker.Port, MqttConnectionOptions.Default with { ClientId = "explorer-7" }),
			cancellationToken);

		await using (session)
		{
			Assert.Equal("explorer-7", connection.Info.ClientId);
			Assert.Equal("explorer-7", Assert.Single(broker.Connects).ClientId);
		}
	}

	[Fact(Timeout = MqttHarness.TestTimeoutMilliseconds)]
	public async Task Connect_WithASavedLogin_SendsItInTheConnectPacket()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackBroker broker = await LoopbackBroker.StartAsync("operator", "s3cret");
		FakeCredentialSource credentials = new("operator", "s3cret");

		(IProtocolSession session, IMqttConnection connection) = await MqttHarness.ConnectAsync(
			MqttHarness.CreateProvider(),
			MqttHarness.CreateContext(broker.Port, credentials: credentials),
			cancellationToken);

		await using (session)
		{
			Assert.Equal(MqttConnectionState.Connected, connection.State);
			Assert.Equal("operator", Assert.Single(broker.Connects).UserName);
			Assert.Equal(1, credentials.GetCount);
			Assert.Equal(0, credentials.RetryCount);
		}
	}

	[Fact(Timeout = MqttHarness.TestTimeoutMilliseconds)]
	public async Task Connect_WithAWrongPassword_AsksAgainAndTheSecondAttemptConnects()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackBroker broker = await LoopbackBroker.StartAsync("operator", "s3cret");
		FakeCredentialSource credentials = new("operator", "wrong", retryPasswords: ["s3cret"]);

		(IProtocolSession session, IMqttConnection connection) = await MqttHarness.ConnectAsync(
			MqttHarness.CreateProvider(),
			MqttHarness.CreateContext(broker.Port, credentials: credentials),
			cancellationToken);

		await using (session)
		{
			Assert.Equal(MqttConnectionState.Connected, connection.State);
			Assert.Equal(1, credentials.RetryCount);

			// A broker refuses the whole CONNECT rather than asking again, so the retry is a second connection.
			Assert.Equal(2, broker.Connects.Count);
			Assert.Contains("refused the login", Assert.Single(credentials.RetryReasons), StringComparison.Ordinal);
		}
	}

	[Fact(Timeout = MqttHarness.TestTimeoutMilliseconds)]
	public async Task Connect_WithAWrongPasswordAndNoRetry_FailsAsAnAuthenticationFailure()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackBroker broker = await LoopbackBroker.StartAsync("operator", "s3cret");
		FakeCredentialSource credentials = new("operator", "wrong");

		ProtocolConnectException failure = await Assert.ThrowsAsync<ProtocolConnectException>(
			() => MqttHarness.CreateProvider().ConnectAsync(MqttHarness.CreateContext(broker.Port, credentials: credentials), cancellationToken));

		Assert.Equal(ConnectFailure.AuthenticationFailed, failure.Failure);
		Assert.Equal(1, credentials.RetryCount);
	}

	[Fact(Timeout = MqttHarness.TestTimeoutMilliseconds)]
	public async Task Connect_StopsAskingAfterTheAttemptLimit()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackBroker broker = await LoopbackBroker.StartAsync("operator", "s3cret");
		FakeCredentialSource credentials = new("operator", "wrong", retryPasswords: ["wrong", "wrong", "wrong", "s3cret"]);
		FakeSettingsService settings = MqttHarness.FastSettings(new MqttSettings { AuthenticationAttempts = 2 });

		await Assert.ThrowsAsync<ProtocolConnectException>(
			() => MqttHarness.CreateProvider(settings).ConnectAsync(MqttHarness.CreateContext(broker.Port, credentials: credentials), cancellationToken));

		Assert.Equal(1, credentials.RetryCount);
		Assert.Equal(2, broker.Connects.Count);
	}

	[Fact(Timeout = MqttHarness.TestTimeoutMilliseconds)]
	public async Task Connect_WithAKeyBasedLogin_SaysTheProtocolHasNoSuchThing()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackBroker broker = await LoopbackBroker.StartAsync();
		FakeCredentialSource credentials = new("operator", method: AuthenticationMethod.PublicKey);

		ProtocolConnectException failure = await Assert.ThrowsAsync<ProtocolConnectException>(
			() => MqttHarness.CreateProvider().ConnectAsync(MqttHarness.CreateContext(broker.Port, credentials: credentials), cancellationToken));

		Assert.Equal(ConnectFailure.AuthenticationFailed, failure.Failure);
		Assert.Contains("key based logins", failure.Message, StringComparison.Ordinal);
		Assert.Empty(broker.Connects);
	}

	[Fact(Timeout = MqttHarness.TestTimeoutMilliseconds)]
	public async Task Connect_ToAPortNothingListensOn_FailsAsUnreachable()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackBroker broker = await LoopbackBroker.StartAsync();
		int dead = broker.Port;
		await broker.DisposeAsync();

		ProtocolConnectException failure = await Assert.ThrowsAsync<ProtocolConnectException>(
			() => MqttHarness.CreateProvider().ConnectAsync(MqttHarness.CreateContext(dead), cancellationToken));

		Assert.Equal(ConnectFailure.HostUnreachable, failure.Failure);
		Assert.Contains("1883", failure.Message, StringComparison.Ordinal);
	}

	[Fact(Timeout = MqttHarness.TestTimeoutMilliseconds)]
	public async Task Connect_WithTheSubscriptionsOfTheConnection_SendsTheOnesMarkedToOpenWithIt()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackBroker broker = await LoopbackBroker.StartAsync();
		MqttConnectionOptions options = MqttConnectionOptions.Default with
		{
			Subscriptions =
			[
				MqttSubscription.Create() with { TopicFilter = "auto/#", QualityOfService = MqttQos.AtLeastOnce },
				MqttSubscription.Create() with { TopicFilter = "manual/#", SubscribeWithSession = false },
			],
		};

		(IProtocolSession session, IMqttConnection connection) = await MqttHarness.ConnectAsync(
			MqttHarness.CreateProvider(),
			MqttHarness.CreateContext(broker.Port, options),
			cancellationToken);

		await using (session)
		{
			Assert.Equal(["auto/#"], broker.Subscribed);
			Assert.Equal(2, connection.Subscriptions.Count);

			MqttSubscriptionStatus active = connection.Subscriptions.Single(status => status.Subscription.TopicFilter == "auto/#");
			Assert.Equal(MqttSubscriptionState.Active, active.State);
			Assert.Equal(MqttQos.AtLeastOnce, active.GrantedQos);
			Assert.False(active.IsSessionOnly);

			MqttSubscriptionStatus idle = connection.Subscriptions.Single(status => status.Subscription.TopicFilter == "manual/#");
			Assert.Equal(MqttSubscriptionState.Idle, idle.State);
		}
	}

	[Fact(Timeout = MqttHarness.TestTimeoutMilliseconds)]
	public async Task Session_ExposesOnlyTheBrokerFeature()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackBroker broker = await LoopbackBroker.StartAsync();

		await using IProtocolSession session = await MqttHarness.CreateProvider()
			.ConnectAsync(MqttHarness.CreateContext(broker.Port), cancellationToken);

		Assert.NotNull(session.GetFeature<IMqttConnection>());
		Assert.Null(session.GetFeature<Abstractions.Terminal.ITerminalChannel>());
		Assert.Null(session.GetFeature<Abstractions.FileSystem.IFileSystemFeature>());
	}

	[Fact(Timeout = MqttHarness.TestTimeoutMilliseconds)]
	public async Task Dispose_ClosesTheConnectionAndCompletesTheSession()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackBroker broker = await LoopbackBroker.StartAsync();

		(IProtocolSession session, IMqttConnection connection) = await MqttHarness.ConnectAsync(
			MqttHarness.CreateProvider(),
			MqttHarness.CreateContext(broker.Port),
			cancellationToken);

		await session.DisposeAsync();

		Assert.Equal(MqttConnectionState.Closed, connection.State);
		await session.Completion.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
	}

	[Fact(Timeout = MqttHarness.TestTimeoutMilliseconds)]
	public async Task Session_WhenTheBrokerClosesTheConnection_EndsWithAReasonAndNoLiveSubscriptions()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackBroker broker = await LoopbackBroker.StartAsync();
		MqttConnectionOptions options = MqttConnectionOptions.Default with
		{
			Subscriptions = [MqttSubscription.Create() with { TopicFilter = "a/#" }],
		};

		(IProtocolSession session, IMqttConnection connection) = await MqttHarness.ConnectAsync(
			MqttHarness.CreateProvider(),
			MqttHarness.CreateContext(broker.Port, options),
			cancellationToken);

		await using (session)
		{
			Assert.Contains(connection.Subscriptions, status => status.State == MqttSubscriptionState.Active);

			await broker.DisconnectClientsAsync();
			await Eventually.TrueAsync(() => connection.State == MqttConnectionState.Closed, "the connection is closed");

			Assert.NotNull(connection.CloseReason);

			// A DISCONNECT from the broker is a clean close, so the session completes rather than faulting; the
			// shell ends the tab either way.
			await session.Completion.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

			// The subscriptions are no longer live, so their rows say so rather than looking active.
			Assert.DoesNotContain(connection.Subscriptions, status => status.State == MqttSubscriptionState.Active);
		}
	}
}
