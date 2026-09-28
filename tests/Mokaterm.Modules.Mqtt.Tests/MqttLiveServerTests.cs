using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Mqtt.Tests.Broker;
using Mokaterm.Modules.Mqtt.Tests.Fakes;

namespace Mokaterm.Modules.Mqtt.Tests;

/// <summary>
/// Runs against a real broker when MOKATERM_TEST_MQTT is set to <c>user:password@host:port</c>, with the login and
/// the port optional (<c>host</c> alone connects to 1883 with no login). Add <c>?tls=true</c> for 8883, and
/// <c>?transport=WebSocket</c> with an optional <c>&amp;path=/mqtt</c> for a WebSocket listener.
/// </summary>
public sealed class MqttLiveServerTests
{
	private const string Variable = "MOKATERM_TEST_MQTT";

	[Fact(Timeout = MqttHarness.TestTimeoutMilliseconds)]
	public async Task Connect_SubscribesPublishesAndReadsItBack()
	{
		string setting = LiveEndpoint.Require(Variable, $"Set {Variable}=user:password@host:port to run against a real MQTT broker.");

		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		LiveEndpoint target = LiveEndpoint.Parse(setting, MqttConnectionOptions.Default.DefaultPort);
		MqttConnectionOptions options = ReadOptions(target);
		FakeCredentialSource credentials = new(
			target.User,
			target.Password,
			target.User is null ? AuthenticationMethod.Anonymous : AuthenticationMethod.Password);

		// A real broker's certificate is whatever it is, so the test trusts what it presents.
		ProtocolConnectContext context = MqttHarness.CreateContext(
			target.PortWasNamed ? target.Port : options.DefaultPort,
			options,
			credentials,
			address: target.Host,
			verifier: FakeHostVerifier.Accepting());

		(IProtocolSession session, IMqttConnection connection) = await MqttHarness.ConnectAsync(
			MqttHarness.CreateProvider(),
			context,
			cancellationToken);

		await using (session)
		{
			Assert.Equal(MqttConnectionState.Connected, connection.State);

			string topic = $"mokaterm/test/{Guid.NewGuid():N}";
			MqttSubscriptionStatus status = await connection.SubscribeAsync(
				MqttSubscription.Create() with { TopicFilter = topic, QualityOfService = MqttQos.AtLeastOnce },
				cancellationToken);
			Assert.Equal(MqttSubscriptionState.Active, status.State);

			MqttPublishOutcome outcome = await connection.PublishAsync(
				MqttPublishRequest.FromText(topic, "hello from mokaterm", MqttQos.AtLeastOnce, retain: false),
				cancellationToken);
			Assert.True(outcome.IsSuccess);

			await Eventually.TrueAsync(
				() => connection.Messages.Messages(topic, 10).Count > 0,
				"the published message has come back");
		}
	}

	/// <summary>The broker options the variable's query asks for: TLS, the transport and its path, the MQTT level.</summary>
	private static MqttConnectionOptions ReadOptions(LiveEndpoint endpoint)
	{
		MqttConnectionOptions options = MqttConnectionOptions.Default with
		{
			UseTls = endpoint.Flag("tls"),
			Transport = endpoint.Option("transport", MqttConnectionOptions.Default.Transport),
			Protocol = endpoint.Option("protocol", MqttConnectionOptions.Default.Protocol),
		};

		return endpoint.Option("path") is { Length: > 0 } path && MqttConnectionOptions.IsValidPath(path)
			? options with { Path = path }
			: options;
	}
}
