using Microsoft.Extensions.Logging.Abstractions;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;
using Mokaterm.Modules.Mqtt.Tests.Fakes;

namespace Mokaterm.Modules.Mqtt.Tests.Broker;

/// <summary>Builds providers, connect contexts and sessions for the broker tests.</summary>
internal static class MqttHarness
{
	public const int TestTimeoutMilliseconds = TestTimeouts.NetworkMilliseconds;

	public static MqttProtocolProvider CreateProvider(FakeSettingsService? settings = null, TimeProvider? timeProvider = null) =>
		new(settings ?? FastSettings(), timeProvider ?? TimeProvider.System, NullLoggerFactory.Instance)
		{
			// A test never waits two seconds for a certificate decision.
			CertificateDecisionWait = TimeSpan.FromMilliseconds(50),
		};

	/// <summary>Settings that keep the tests quick and their caps small enough to reach.</summary>
	public static FakeSettingsService FastSettings(MqttSettings? settings = null)
	{
		FakeSettingsService service = new();
		service.Set(settings ?? new MqttSettings { ConnectTimeoutSeconds = 10 });
		return service;
	}

	public static ProtocolConnectContext CreateContext(
		int port,
		MqttConnectionOptions? options = null,
		ICredentialSource? credentials = null,
		RecordingProgress<string>? status = null,
		string address = "127.0.0.1",
		IHostIdentityVerifier? verifier = null) =>
		new ConnectContext
		{
			ProtocolId = MqttProtocolProvider.ProtocolId,
			Port = port,
			Address = address,
			Credentials = credentials ?? new FakeCredentialSource(method: AuthenticationMethod.Anonymous),
			Verifier = verifier ?? new RefusingHostVerifier(),
			Options = (options ?? MqttConnectionOptions.Default).ApplyTo(ProtocolOptions.Empty),
			Status = status,
		}.Build();

	/// <summary>Opens a session and returns it with its broker feature.</summary>
	public static async Task<(IProtocolSession Session, IMqttConnection Connection)> ConnectAsync(
		MqttProtocolProvider provider,
		ProtocolConnectContext context,
		CancellationToken cancellationToken)
	{
		IProtocolSession session = await provider.ConnectAsync(context, cancellationToken);
		IMqttConnection connection = session.GetFeature<IMqttConnection>()
			?? throw new InvalidOperationException("An MQTT session must expose a broker connection.");
		return (session, connection);
	}
}
