using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using MQTTnet;

namespace Mokaterm.Modules.Mqtt.Protocol;

/// <summary>Builds the MQTTnet client options for one connection attempt.</summary>
internal static class MqttClientOptionsFactory
{
	/// <summary>
	/// What a connection that does not start clean asks the broker to keep its session for. MQTT 5 drops a session
	/// the moment the client disconnects unless the CONNECT names an expiry, so "keep my subscriptions across a
	/// reconnect" needs one; an hour outlives a reconnect and stops a forgotten session collecting forever.
	/// </summary>
	public const uint KeptSessionSeconds = 3600;

	public static MqttClientOptions Create(
		MqttEndpoint endpoint,
		MqttConnectionOptions connection,
		string clientId,
		MqttSecretCredentials? credentials,
		MqttCertificateTrust? trust,
		TimeSpan timeout)
	{
		ArgumentNullException.ThrowIfNull(endpoint);
		ArgumentNullException.ThrowIfNull(connection);

		MqttClientOptionsBuilder builder = new MqttClientOptionsBuilder()
			.WithClientId(clientId)
			.WithProtocolVersion(MqttWire.ToWire(connection.Protocol))
			.WithCleanStart(connection.CleanStart)
			.WithTimeout(timeout);

		if (!connection.CleanStart)
		{
			builder = builder.WithSessionExpiryInterval(KeptSessionSeconds);
		}

		builder = connection.KeepAliveSeconds > 0
			? builder.WithKeepAlivePeriod(TimeSpan.FromSeconds(connection.KeepAliveSeconds))
			: builder.WithNoKeepAlive();

		if (credentials is not null)
		{
			builder = builder.WithCredentials(credentials);
		}

		if (endpoint.Transport == MqttTransport.WebSocket)
		{
			builder = builder.WithWebSocketServer(socket => socket.WithUri(endpoint.WebSocketUri));
		}
		else
		{
			builder = builder.WithTcpServer(endpoint.Host, endpoint.Port);
		}

		return endpoint.UseTls ? builder.WithTlsOptions(tls => Configure(tls, endpoint, trust)).Build() : builder.Build();
	}

	private static void Configure(MqttClientTlsOptionsBuilder tls, MqttEndpoint endpoint, MqttCertificateTrust? trust)
	{
		tls.UseTls()

			// SNI and the name the certificate is checked against.
			.WithTargetHost(endpoint.Host)

			// The operating system picks the versions, and a broker certificate is often self-signed with no
			// revocation list to fetch. The handler below is what decides, so neither of these is a hole.
			.WithSslProtocols(SslProtocols.None)
			.WithRevocationMode(X509RevocationMode.NoCheck);

		if (trust is not null)
		{
			tls.WithCertificateValidationHandler(trust.Validate);
		}
		else
		{
			// Nothing to judge the certificate with, so nothing is accepted: a connection without a verifier is a
			// programming error, not a reason to trust a broker.
			tls.WithCertificateValidationHandler(static _ => false);
		}
	}
}
