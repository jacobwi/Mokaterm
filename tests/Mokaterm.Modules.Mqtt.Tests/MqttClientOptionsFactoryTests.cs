using System.Net;
using Mokaterm.Abstractions.Security;
using Mokaterm.Modules.Mqtt.Protocol;
using MQTTnet;
using MQTTnet.Formatter;

namespace Mokaterm.Modules.Mqtt.Tests;

public sealed class MqttClientOptionsFactoryTests
{
	[Fact]
	public void Create_OverTcp_PointsAtTheHostAndPortWithNoTls()
	{
		MqttClientOptions options = Create(MqttConnectionOptions.Default);

		MqttClientTcpOptions tcp = Assert.IsType<MqttClientTcpOptions>(options.ChannelOptions);
		Assert.Equal(new DnsEndPoint("broker.test", 1883), tcp.RemoteEndpoint);
		Assert.False(tcp.TlsOptions.UseTls);
		Assert.Equal("client-1", options.ClientId);
		Assert.Equal(MqttProtocolVersion.V500, options.ProtocolVersion);
		Assert.True(options.CleanSession);
		Assert.Equal(TimeSpan.FromSeconds(MqttConnectionOptions.DefaultKeepAliveSeconds), options.KeepAlivePeriod);
		Assert.Equal(TimeSpan.FromSeconds(15), options.Timeout);
		Assert.Null(options.Credentials);
	}

	[Fact]
	public void Create_WithTls_TurnsItOnWithTheHostAsTheTargetAndAHandlerThatDecides()
	{
		using MqttCertificateTrust trust = new(
			FakeHostVerifier.Accepting(),
			"broker.test",
			8883,
			Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

		MqttClientOptions options = Create(MqttConnectionOptions.Default with { UseTls = true }, port: 8883, trust: trust);

		MqttClientTcpOptions tcp = Assert.IsType<MqttClientTcpOptions>(options.ChannelOptions);
		Assert.True(tcp.TlsOptions.UseTls);
		Assert.Equal("broker.test", tcp.TlsOptions.TargetHost);
		Assert.NotNull(tcp.TlsOptions.CertificateValidationHandler);
		Assert.Equal(X509RevocationMode(), tcp.TlsOptions.RevocationMode);

		// Nothing is waved through: the handler is what decides, so this stays off.
		Assert.False(tcp.TlsOptions.AllowUntrustedCertificates);
	}

	[Fact]
	public void Create_WithTlsAndNoVerifier_RefusesEveryCertificate()
	{
		MqttClientOptions options = Create(MqttConnectionOptions.Default with { UseTls = true }, port: 8883, trust: null);

		MqttClientTcpOptions tcp = Assert.IsType<MqttClientTcpOptions>(options.ChannelOptions);
		Assert.NotNull(tcp.TlsOptions.CertificateValidationHandler);
		Assert.False(tcp.TlsOptions.CertificateValidationHandler!(null!));
	}

	[Fact]
	public void Create_OverWebSockets_BuildsTheUriFromTheSchemePortAndPath()
	{
		MqttClientOptions options = Create(
			MqttConnectionOptions.Default with { Transport = MqttTransport.WebSocket, UseTls = true, Path = "/broker/ws" },
			port: 443);

		MqttClientWebSocketOptions socket = Assert.IsType<MqttClientWebSocketOptions>(options.ChannelOptions);
		Assert.Equal("wss://broker.test:443/broker/ws", socket.Uri);
		Assert.True(socket.TlsOptions.UseTls);
	}

	[Fact]
	public void Create_OverWebSocketsWithoutTls_UsesTheWsScheme()
	{
		MqttClientOptions options = Create(
			MqttConnectionOptions.Default with { Transport = MqttTransport.WebSocket },
			port: 8083);

		MqttClientWebSocketOptions socket = Assert.IsType<MqttClientWebSocketOptions>(options.ChannelOptions);
		Assert.Equal("ws://broker.test:8083/mqtt", socket.Uri);
	}

	[Fact]
	public void Create_WithKeepAliveOff_SendsNoPings() =>
		Assert.Equal(TimeSpan.Zero, Create(MqttConnectionOptions.Default with { KeepAliveSeconds = 0 }).KeepAlivePeriod);

	[Fact]
	public void Create_WithoutACleanStart_AsksTheBrokerToKeepTheSession()
	{
		MqttClientOptions options = Create(MqttConnectionOptions.Default with { CleanStart = false });

		Assert.False(options.CleanSession);

		// MQTT 5 drops a session at disconnect unless the CONNECT names an expiry, so keeping one needs both.
		Assert.Equal(MqttClientOptionsFactory.KeptSessionSeconds, options.SessionExpiryInterval);
	}

	[Fact]
	public void Create_WithACleanStart_AsksForNoSessionAtAll() =>
		Assert.Equal(0u, Create(MqttConnectionOptions.Default).SessionExpiryInterval);

	[Theory]
	[InlineData(MqttProtocolLevel.V500, MqttProtocolVersion.V500)]
	[InlineData(MqttProtocolLevel.V311, MqttProtocolVersion.V311)]
	[InlineData(MqttProtocolLevel.V310, MqttProtocolVersion.V310)]
	public void Create_SendsTheVersionTheConnectionAsksFor(MqttProtocolLevel level, MqttProtocolVersion expected) =>
		Assert.Equal(expected, Create(MqttConnectionOptions.Default with { Protocol = level }).ProtocolVersion);

	[Fact]
	public void Create_WithALogin_HandsMqttnetAProviderRatherThanTheBytes()
	{
		using MqttSecretCredentials credentials = new("operator", SecretBuffer.FromString("s3cret"));

		MqttClientOptions options = Create(MqttConnectionOptions.Default, credentials: credentials);

		Assert.Same(credentials, options.Credentials);
		Assert.Equal("operator", options.Credentials!.GetUserName(options));
		Assert.Equal("s3cret", System.Text.Encoding.UTF8.GetString(options.Credentials.GetPassword(options)!));
	}

	private static System.Security.Cryptography.X509Certificates.X509RevocationMode X509RevocationMode() =>
		System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck;

	private static MqttClientOptions Create(
		MqttConnectionOptions connection,
		int port = 1883,
		MqttSecretCredentials? credentials = null,
		MqttCertificateTrust? trust = null) =>
		MqttClientOptionsFactory.Create(
			MqttEndpoint.Create("broker.test", port, connection),
			connection,
			"client-1",
			credentials,
			trust,
			TimeSpan.FromSeconds(15));
}
