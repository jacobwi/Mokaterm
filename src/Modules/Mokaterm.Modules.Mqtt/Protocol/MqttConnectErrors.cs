using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Authentication;
using Mokaterm.Abstractions.Protocols;
using MQTTnet;
using MQTTnet.Exceptions;

namespace Mokaterm.Modules.Mqtt.Protocol;

/// <summary>Turns connect failures into <see cref="ProtocolConnectException"/> with messages written for the user.</summary>
internal static class MqttConnectErrors
{
	private static readonly SocketFailureHints Hints = new()
	{
		Refused = "Check the port: MQTT listens on 1883, and on 8883 with TLS.",
		Reset = "A broker that expects TLS closes a plain connection this way.",
	};

	/// <summary>
	/// What a broker's CONNACK means. MQTTnet does not throw for a refused CONNACK: it returns the result with the
	/// code, so this is read from <c>MqttClientConnectResult.ResultCode</c> and not from an exception.
	/// </summary>
	public static ProtocolConnectException Describe(MqttClientConnectResult result, string endpoint)
	{
		ArgumentNullException.ThrowIfNull(result);
		string reason = string.IsNullOrWhiteSpace(result.ReasonString) ? "" : $" The broker said: {result.ReasonString}";
		return result.ResultCode switch
		{
			MqttClientConnectResultCode.BadUserNameOrPassword or MqttClientConnectResultCode.NotAuthorized =>
				new ProtocolConnectException(ConnectFailure.AuthenticationFailed, $"{endpoint} refused the login.{reason}"),
			MqttClientConnectResultCode.ClientIdentifierNotValid =>
				new ProtocolConnectException(
					ConnectFailure.ProtocolError,
					$"{endpoint} refused the client id. Brokers may limit it to 23 characters of letters and digits.{reason}"),
			MqttClientConnectResultCode.UnsupportedProtocolVersion =>
				new ProtocolConnectException(
					ConnectFailure.ProtocolError,
					$"{endpoint} does not speak the MQTT version this connection asked for. Try MQTT 3.1.1.{reason}"),
			MqttClientConnectResultCode.ServerUnavailable or MqttClientConnectResultCode.ServerBusy or MqttClientConnectResultCode.ServerMoved =>
				new ProtocolConnectException(ConnectFailure.HostUnreachable, $"{endpoint} is not taking connections right now.{reason}"),
			MqttClientConnectResultCode.Banned =>
				new ProtocolConnectException(ConnectFailure.AuthenticationFailed, $"{endpoint} has banned this client.{reason}"),
			MqttClientConnectResultCode.QuotaExceeded =>
				new ProtocolConnectException(ConnectFailure.ProtocolError, $"{endpoint} is at its connection limit.{reason}"),
			_ => new ProtocolConnectException(
				ConnectFailure.ProtocolError,
				$"{endpoint} refused the connection ({result.ResultCode}).{reason}"),
		};
	}

	/// <param name="certificateRefusals">
	/// How many handshakes the trust decision refused. A refused handshake reaches this as an ordinary communication
	/// failure, so the count is the only thing that tells the two apart.
	/// </param>
	public static async Task<ProtocolConnectException> DescribeAsync(
		Exception exception,
		MqttEndpoint endpoint,
		int certificateRefusals,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(exception);
		ArgumentNullException.ThrowIfNull(endpoint);
		string display = endpoint.Display;
		if (certificateRefusals > 0)
		{
			return new ProtocolConnectException(
				ConnectFailure.HostIdentityRejected,
				$"The TLS certificate presented by {display} was not trusted.",
				exception);
		}

		// MQTTnet's own connect timeout (MqttClientOptions.Timeout) is what ends a connect that hangs, and it comes
		// back as this exception, so nothing here needs a deadline of its own.
		return exception switch
		{
			ProtocolConnectException connect => connect,
			MqttCommunicationTimedOutException => new(ConnectFailure.Timeout, $"{display} did not answer in time.", exception),
			MqttProtocolViolationException violation => new(ConnectFailure.ProtocolError, violation.Message, violation),
			MqttConfigurationException configuration => new(ConnectFailure.ProtocolError, configuration.Message, configuration),
			_ => await FromTransportAsync(exception, endpoint, display, cancellationToken),
		};
	}

	private static async Task<ProtocolConnectException> FromTransportAsync(
		Exception exception,
		MqttEndpoint endpoint,
		string display,
		CancellationToken cancellationToken)
	{
		// MQTTnet wraps every transport failure in MqttCommunicationException, so the cause is always the inner one.
		Exception cause = exception.GetBaseException();
		return cause switch
		{
			SocketException socket => await FromSocketErrorAsync(socket, endpoint, display, cancellationToken),
			AuthenticationException authentication => new(
				ConnectFailure.ProtocolError,
				$"The TLS handshake with {display} failed: {authentication.Message}",
				exception),
			WebSocketException => new(
				ConnectFailure.HostUnreachable,
				$"{display} did not accept a WebSocket connection. Check the path and whether the broker has its WebSocket listener turned on.",
				exception),
			HttpRequestException => new(
				ConnectFailure.HostUnreachable,
				$"{display} did not accept a WebSocket connection. Check the path and whether the broker has its WebSocket listener turned on.",
				exception),
			_ => new(ConnectFailure.Unknown, $"Could not connect to {display}: {cause.Message}", exception),
		};
	}

	private static async Task<ProtocolConnectException> FromSocketErrorAsync(
		SocketException socket,
		MqttEndpoint endpoint,
		string display,
		CancellationToken cancellationToken) =>
		(await SocketFailures.DescribeAsync(socket, endpoint.Host, display, Hints, cancellationToken)).ToException(socket);
}
