using System.Net.Sockets;
using System.Security.Authentication;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Mqtt.Protocol;
using MQTTnet;
using MQTTnet.Exceptions;

namespace Mokaterm.Modules.Mqtt.Tests;

public sealed class MqttConnectErrorsTests
{
	[Theory]
	[InlineData(MqttClientConnectResultCode.BadUserNameOrPassword, ConnectFailure.AuthenticationFailed)]
	[InlineData(MqttClientConnectResultCode.NotAuthorized, ConnectFailure.AuthenticationFailed)]
	[InlineData(MqttClientConnectResultCode.Banned, ConnectFailure.AuthenticationFailed)]
	[InlineData(MqttClientConnectResultCode.ClientIdentifierNotValid, ConnectFailure.ProtocolError)]
	[InlineData(MqttClientConnectResultCode.UnsupportedProtocolVersion, ConnectFailure.ProtocolError)]
	[InlineData(MqttClientConnectResultCode.ServerUnavailable, ConnectFailure.HostUnreachable)]
	[InlineData(MqttClientConnectResultCode.ServerBusy, ConnectFailure.HostUnreachable)]
	[InlineData(MqttClientConnectResultCode.QuotaExceeded, ConnectFailure.ProtocolError)]
	[InlineData(MqttClientConnectResultCode.UnspecifiedError, ConnectFailure.ProtocolError)]
	public void Describe_MapsEveryConnackCodeToAFailureTheShellKnows(MqttClientConnectResultCode code, ConnectFailure expected)
	{
		ProtocolConnectException failure = MqttConnectErrors.Describe(new MqttClientConnectResult { ResultCode = code }, "broker:1883");

		Assert.Equal(expected, failure.Failure);
		Assert.Contains("broker:1883", failure.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Describe_AddsWhateverTheBrokerSaidAboutIt()
	{
		ProtocolConnectException failure = MqttConnectErrors.Describe(
			new MqttClientConnectResult { ResultCode = MqttClientConnectResultCode.NotAuthorized, ReasonString = "acl denied" },
			"broker:1883");

		Assert.Contains("acl denied", failure.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task DescribeAsync_WithARefusedCertificate_ReportsTheIdentityAndNotTheTransport()
	{
		MqttEndpoint endpoint = Endpoint();

		ProtocolConnectException failure = await MqttConnectErrors.DescribeAsync(
			new MqttCommunicationException(new AuthenticationException("rejected")),
			endpoint,
			certificateRefusals: 1,
			TestContext.Current.CancellationToken);

		Assert.Equal(ConnectFailure.HostIdentityRejected, failure.Failure);
		Assert.Contains("certificate", failure.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task DescribeAsync_MapsMqttnetsOwnConnectTimeout()
	{
		ProtocolConnectException failure = await MqttConnectErrors.DescribeAsync(
			new MqttCommunicationTimedOutException(),
			Endpoint(),
			certificateRefusals: 0,
			TestContext.Current.CancellationToken);

		Assert.Equal(ConnectFailure.Timeout, failure.Failure);
		Assert.Contains("in time", failure.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task DescribeAsync_MapsATimeoutFromTheSocketItself()
	{
		ProtocolConnectException failure = await MqttConnectErrors.DescribeAsync(
			new MqttCommunicationException(new SocketException((int)SocketError.TimedOut)),
			Endpoint(),
			certificateRefusals: 0,
			TestContext.Current.CancellationToken);

		Assert.Equal(ConnectFailure.Timeout, failure.Failure);
	}

	[Fact]
	public async Task DescribeAsync_MapsARefusedSocketAndNamesThePorts()
	{
		ProtocolConnectException failure = await MqttConnectErrors.DescribeAsync(
			new MqttCommunicationException(new SocketException((int)SocketError.ConnectionRefused)),
			Endpoint(),
			certificateRefusals: 0,
			TestContext.Current.CancellationToken);

		Assert.Equal(ConnectFailure.HostUnreachable, failure.Failure);
		Assert.Equal("broker.test:1883 refused the connection. Check the port: MQTT listens on 1883, and on 8883 with TLS.", failure.Message);
	}

	[Fact]
	public async Task DescribeAsync_MapsAnUnroutableBroker()
	{
		ProtocolConnectException failure = await MqttConnectErrors.DescribeAsync(
			new MqttCommunicationException(new SocketException((int)SocketError.HostUnreachable)),
			Endpoint(),
			certificateRefusals: 0,
			TestContext.Current.CancellationToken);

		Assert.Equal(ConnectFailure.HostUnreachable, failure.Failure);
		Assert.Equal("There is no route to broker.test:1883.", failure.Message);
	}

	[Fact]
	public async Task DescribeAsync_ARefusedSocketOverWebSockets_NamesTheWebSocketAddress()
	{
		MqttEndpoint endpoint = new()
		{
			Host = "broker.test",
			Port = 8083,
			Transport = MqttTransport.WebSocket,
			UseTls = false,
			Path = "/mqtt",
		};

		ProtocolConnectException failure = await MqttConnectErrors.DescribeAsync(
			new MqttCommunicationException(new SocketException((int)SocketError.ConnectionRefused)),
			endpoint,
			certificateRefusals: 0,
			TestContext.Current.CancellationToken);

		Assert.StartsWith("ws://broker.test:8083/mqtt refused the connection.", failure.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task DescribeAsync_MapsAResetSocketAndMentionsTls()
	{
		ProtocolConnectException failure = await MqttConnectErrors.DescribeAsync(
			new MqttCommunicationException(new SocketException((int)SocketError.ConnectionReset)),
			Endpoint(),
			certificateRefusals: 0,
			TestContext.Current.CancellationToken);

		Assert.Equal(ConnectFailure.ProtocolError, failure.Failure);
		Assert.Equal(
			"broker.test:1883 closed the connection unexpectedly. A broker that expects TLS closes a plain connection this way.",
			failure.Message);
	}

	[Fact]
	public async Task DescribeAsync_MapsAFailedTlsHandshake()
	{
		ProtocolConnectException failure = await MqttConnectErrors.DescribeAsync(
			new MqttCommunicationException(new AuthenticationException("no cipher")),
			Endpoint(),
			certificateRefusals: 0,
			TestContext.Current.CancellationToken);

		Assert.Equal(ConnectFailure.ProtocolError, failure.Failure);
		Assert.Contains("TLS handshake", failure.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task DescribeAsync_MapsAWebSocketFailureToTheAddressAndThePath()
	{
		MqttEndpoint endpoint = new()
		{
			Host = "broker.test",
			Port = 8083,
			Transport = MqttTransport.WebSocket,
			UseTls = false,
			Path = "/mqtt",
		};

		ProtocolConnectException failure = await MqttConnectErrors.DescribeAsync(
			new MqttCommunicationException(new System.Net.WebSockets.WebSocketException("refused")),
			endpoint,
			certificateRefusals: 0,
			TestContext.Current.CancellationToken);

		Assert.Equal(ConnectFailure.HostUnreachable, failure.Failure);
		Assert.Contains("WebSocket", failure.Message, StringComparison.Ordinal);
		Assert.Contains("ws://broker.test:8083/mqtt", failure.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task DescribeAsync_KeepsAProtocolConnectExceptionAsItIs()
	{
		ProtocolConnectException original = new(ConnectFailure.Cancelled, "cancelled");

		ProtocolConnectException described = await MqttConnectErrors.DescribeAsync(
			original,
			Endpoint(),
			certificateRefusals: 0,
			TestContext.Current.CancellationToken);

		Assert.Same(original, described);
	}

	[Fact]
	public async Task DescribeAsync_MapsAnUnknownHost()
	{
		MqttEndpoint endpoint = new()
		{
			Host = "no-such-host.invalid",
			Port = 1883,
			Transport = MqttTransport.Tcp,
			UseTls = false,
		};

		ProtocolConnectException failure = await MqttConnectErrors.DescribeAsync(
			new MqttCommunicationException(new SocketException((int)SocketError.HostNotFound)),
			endpoint,
			certificateRefusals: 0,
			TestContext.Current.CancellationToken);

		Assert.Equal(ConnectFailure.HostUnreachable, failure.Failure);
		Assert.Contains("no-such-host.invalid", failure.Message, StringComparison.Ordinal);
	}

	private static MqttEndpoint Endpoint() => new()
	{
		Host = "broker.test",
		Port = 1883,
		Transport = MqttTransport.Tcp,
		UseTls = false,
	};
}
