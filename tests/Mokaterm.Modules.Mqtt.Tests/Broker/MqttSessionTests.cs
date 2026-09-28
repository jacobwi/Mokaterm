using System.Text;
using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Modules.Mqtt.Tests.Broker;

/// <summary>Subscribing, receiving and publishing against a real broker on a loopback port.</summary>
public sealed class MqttSessionTests
{
	[Fact(Timeout = MqttHarness.TestTimeoutMilliseconds)]
	public async Task Subscribe_FromTheView_TakesTheMessagesThatFollow()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackBroker broker = await LoopbackBroker.StartAsync();
		(IProtocolSession session, IMqttConnection connection) = await ConnectAsync(broker, cancellationToken);
		await using (session)
		{
			MqttSubscriptionStatus status = await connection.SubscribeAsync(
				MqttSubscription.Create() with { TopicFilter = "house/#" },
				cancellationToken);

			Assert.Equal(MqttSubscriptionState.Active, status.State);
			Assert.True(status.IsSessionOnly);

			await broker.PublishAsync("house/kitchen/lamp", "on", retain: true);
			await Eventually.TrueAsync(() => connection.Messages.Received > 0, "the message has arrived");

			MqttMessage message = Assert.Single(connection.Messages.Messages("house/kitchen/lamp", 10));
			Assert.Equal("on", Encoding.UTF8.GetString(message.Payload.Span));
			Assert.True(message.Retain);
			Assert.Equal(1, connection.Messages.TopicCount);

			// The subscription counts what its filter matched, which is how the panel says where traffic comes from.
			await Eventually.TrueAsync(
				() => connection.Subscriptions.Any(entry => entry.Subscription.TopicFilter == "house/#" && entry.Matched == 1),
				"the subscription has counted the message");
		}
	}

	[Fact(Timeout = MqttHarness.TestTimeoutMilliseconds)]
	public async Task Subscribe_WithAFilterTheBrokerNeverSees_FailsWithTheReasonOnItsRow()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackBroker broker = await LoopbackBroker.StartAsync();
		(IProtocolSession session, IMqttConnection connection) = await ConnectAsync(broker, cancellationToken);
		await using (session)
		{
			MqttSubscriptionStatus status = await connection.SubscribeAsync(
				MqttSubscription.Create() with { TopicFilter = "a/#/b" },
				cancellationToken);

			Assert.Equal(MqttSubscriptionState.Failed, status.State);
			Assert.NotNull(status.Error);
			Assert.Empty(broker.Subscribed);

			// The row stays, so the filter can be corrected rather than typed again.
			Assert.Single(connection.Subscriptions);
		}
	}

	[Fact(Timeout = MqttHarness.TestTimeoutMilliseconds)]
	public async Task RemoveSubscription_UnsubscribesAndTakesTheRowWithIt()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackBroker broker = await LoopbackBroker.StartAsync();
		(IProtocolSession session, IMqttConnection connection) = await ConnectAsync(broker, cancellationToken);
		await using (session)
		{
			MqttSubscription subscription = MqttSubscription.Create() with { TopicFilter = "house/#" };
			await connection.SubscribeAsync(subscription, cancellationToken);

			await connection.RemoveSubscriptionAsync(subscription.Id, cancellationToken);

			Assert.Empty(connection.Subscriptions);

			await broker.PublishAsync("house/kitchen/lamp", "on");
			await Task.Delay(200, cancellationToken);
			Assert.Equal(0, connection.Messages.Received);
		}
	}

	[Fact(Timeout = MqttHarness.TestTimeoutMilliseconds)]
	public async Task Publish_SendsTheMessageToTheBroker()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackBroker broker = await LoopbackBroker.StartAsync();
		(IProtocolSession session, IMqttConnection connection) = await ConnectAsync(broker, cancellationToken);
		await using (session)
		{
			MqttPublishOutcome outcome = await connection.PublishAsync(
				MqttPublishRequest.FromText("house/kitchen/lamp/set", "off", MqttQos.AtLeastOnce, retain: true),
				cancellationToken);

			Assert.True(outcome.IsSuccess);
			Assert.Null(outcome.Error);
			await Eventually.TrueAsync(() => broker.Published.Count == 1, "the broker has taken the message");

			(string Topic, string Payload, bool Retain) published = broker.Published[0];
			Assert.Equal("house/kitchen/lamp/set", published.Topic);
			Assert.Equal("off", published.Payload);
			Assert.True(published.Retain);
		}
	}

	[Fact(Timeout = MqttHarness.TestTimeoutMilliseconds)]
	public async Task Publish_ThatTheSessionAlsoSubscribesTo_ComesBackThroughTheStore()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackBroker broker = await LoopbackBroker.StartAsync();
		(IProtocolSession session, IMqttConnection connection) = await ConnectAsync(broker, cancellationToken);
		await using (session)
		{
			await connection.SubscribeAsync(
				MqttSubscription.Create() with { QualityOfService = MqttQos.AtLeastOnce },
				cancellationToken);

			await connection.PublishAsync(MqttPublishRequest.FromText("round/trip", "hello", MqttQos.AtLeastOnce, retain: false), cancellationToken);

			await Eventually.TrueAsync(() => connection.Messages.Received > 0, "the message has come back");
			MqttMessage message = Assert.Single(connection.Messages.Messages("round/trip", 10));
			Assert.Equal("hello", Encoding.UTF8.GetString(message.Payload.Span));
			Assert.Equal(MqttQos.AtLeastOnce, message.QualityOfService);
		}
	}

	[Fact(Timeout = MqttHarness.TestTimeoutMilliseconds)]
	public async Task Publish_ToATopicWithAWildcard_IsRefusedBeforeItReachesTheBroker()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackBroker broker = await LoopbackBroker.StartAsync();
		(IProtocolSession session, IMqttConnection connection) = await ConnectAsync(broker, cancellationToken);
		await using (session)
		{
			MqttPublishOutcome outcome = await connection.PublishAsync(
				MqttPublishRequest.FromText("house/#", "x", MqttQos.AtMostOnce, retain: false),
				cancellationToken);

			Assert.False(outcome.IsSuccess);
			Assert.NotNull(outcome.Error);
			Assert.Empty(broker.Published);
		}
	}

	[Fact(Timeout = MqttHarness.TestTimeoutMilliseconds)]
	public async Task Publish_AfterTheConnectionClosed_SaysSoRatherThanThrowing()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackBroker broker = await LoopbackBroker.StartAsync();
		(IProtocolSession session, IMqttConnection connection) = await ConnectAsync(broker, cancellationToken);
		await using (session)
		{
			await broker.DisconnectClientsAsync();
			await Eventually.TrueAsync(() => connection.State == MqttConnectionState.Closed, "the connection is closed");

			MqttPublishOutcome outcome = await connection.PublishAsync(
				MqttPublishRequest.FromText("a", "b", MqttQos.AtMostOnce, retain: false),
				cancellationToken);

			Assert.False(outcome.IsSuccess);
			Assert.Contains("closed", outcome.Error!, StringComparison.OrdinalIgnoreCase);
		}
	}

	[Fact(Timeout = MqttHarness.TestTimeoutMilliseconds)]
	public async Task Receive_AppliesTheCapsWhereTheMessageArrives()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackBroker broker = await LoopbackBroker.StartAsync();
		(IProtocolSession session, IMqttConnection connection) = await ConnectAsync(
			broker,
			cancellationToken,
			new MqttSettings { MessagesPerTopic = 2, TotalMessages = MqttSettings.MinTotalMessages, LargestPayloadKilobytes = 1 });

		await using (session)
		{
			await connection.SubscribeAsync(MqttSubscription.Create(), cancellationToken);
			for (int index = 1; index <= 5; index++)
			{
				await broker.PublishAsync("busy/topic", index.ToString(System.Globalization.CultureInfo.InvariantCulture));
			}

			await Eventually.TrueAsync(() => connection.Messages.Received >= 5, "every message has arrived");

			// Five arrived, two are kept: the cap is applied on MQTTnet's receive loop and not at draw time.
			Assert.Equal(2, connection.Messages.Kept);
			Assert.Equal(3, connection.Messages.Dropped);
			Assert.Equal(["5", "4"], connection.Messages.Messages("busy/topic", 10).Select(message => Encoding.UTF8.GetString(message.Payload.Span)));
		}
	}

	[Fact(Timeout = MqttHarness.TestTimeoutMilliseconds)]
	public async Task Receive_CutsAPayloadLargerThanTheCapAndMarksIt()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackBroker broker = await LoopbackBroker.StartAsync();
		(IProtocolSession session, IMqttConnection connection) = await ConnectAsync(
			broker,
			cancellationToken,
			new MqttSettings { LargestPayloadKilobytes = 1 });

		await using (session)
		{
			await connection.SubscribeAsync(MqttSubscription.Create(), cancellationToken);
			await broker.PublishAsync("big/one", new string('x', 8192));

			await Eventually.TrueAsync(() => connection.Messages.Received > 0, "the message has arrived");

			MqttMessage message = Assert.Single(connection.Messages.Messages("big/one", 10));
			Assert.True(message.IsTruncated);
			Assert.Equal(1024, message.Payload.Length);
			Assert.Equal(8192, message.PayloadLength);
		}
	}

	[Fact(Timeout = MqttHarness.TestTimeoutMilliseconds)]
	public async Task Receive_KeepsABinaryPayloadAsItArrived()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackBroker broker = await LoopbackBroker.StartAsync();
		(IProtocolSession session, IMqttConnection connection) = await ConnectAsync(broker, cancellationToken);
		await using (session)
		{
			await connection.SubscribeAsync(MqttSubscription.Create(), cancellationToken);
			byte[] payload = [0x00, 0x01, 0xff, 0x7f];
			await broker.PublishAsync("frame", payload);

			await Eventually.TrueAsync(() => connection.Messages.Received > 0, "the frame has arrived");

			MqttMessage message = Assert.Single(connection.Messages.Messages("frame", 10));
			Assert.Equal(payload, message.Payload.ToArray());
		}
	}

	[Fact(Timeout = MqttHarness.TestTimeoutMilliseconds)]
	public async Task Receive_BuildsTheTreeFromWhatArrives()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackBroker broker = await LoopbackBroker.StartAsync();
		(IProtocolSession session, IMqttConnection connection) = await ConnectAsync(broker, cancellationToken);
		await using (session)
		{
			await connection.SubscribeAsync(MqttSubscription.Create(), cancellationToken);
			await broker.PublishAsync("house/kitchen/lamp", "on", retain: true);
			await broker.PublishAsync("house/hall/lock", "locked");

			await Eventually.TrueAsync(() => connection.Messages.TopicCount == 2, "both topics have arrived");

			List<MqttTopicRow> rows = connection.Messages.Rows(_ => true, search: null, byTime: false, limit: 50);

			Assert.Equal(
				["house", "house/hall", "house/hall/lock", "house/kitchen", "house/kitchen/lamp"],
				rows.Select(row => row.Path));
			Assert.True(rows.Single(row => row.Path == "house/kitchen/lamp").HasRetained);
			Assert.Equal("on", rows.Single(row => row.Path == "house/kitchen/lamp").Preview);
		}
	}

	private static Task<(IProtocolSession Session, IMqttConnection Connection)> ConnectAsync(
		LoopbackBroker broker,
		CancellationToken cancellationToken,
		MqttSettings? settings = null) =>
		MqttHarness.ConnectAsync(
			MqttHarness.CreateProvider(MqttHarness.FastSettings(settings)),
			MqttHarness.CreateContext(broker.Port),
			cancellationToken);
}
