using System.Net.Sockets;
using Mokaterm.Abstractions.Protocols;
using Renci.SshNet.Common;
using Renci.SshNet.Messages.Transport;

namespace Mokaterm.Modules.Ssh.Connection;

/// <summary>Turns SSH.NET and socket failures into <see cref="ProtocolConnectException"/> with messages for the user.</summary>
internal static class SshConnectErrors
{
	/// <summary>
	/// The exception to throw for <paramref name="exception"/>. Cancellation and exceptions that are already a
	/// <see cref="ProtocolConnectException"/> come back unchanged.
	/// </summary>
	public static Exception ToConnectException(Exception exception, string host, int port)
	{
		ArgumentNullException.ThrowIfNull(exception);
		string endpoint = HostEndpoint.Format(host, port);
		return exception switch
		{
			OperationCanceledException or ProtocolConnectException => exception,
			SshAuthenticationException => new ProtocolConnectException(ConnectFailure.AuthenticationFailed, AuthenticationMessage(exception), exception),
			SshOperationTimeoutException => new ProtocolConnectException(ConnectFailure.Timeout, $"{endpoint} did not respond in time.", exception),
			SocketException { SocketErrorCode: SocketError.TimedOut } => new ProtocolConnectException(ConnectFailure.Timeout, $"Timed out connecting to {endpoint}.", exception),
			SocketException socket => Unreachable(endpoint, Describe(socket), exception),
			ProxyException => Unreachable(endpoint, $"the proxy refused the connection ({exception.Message})", exception),
			SshConnectionException { InnerException: SocketException socket } => Unreachable(endpoint, Describe(socket), exception),
			SshConnectionException { DisconnectReason: DisconnectReason.ConnectionLost } => Unreachable(endpoint, "the server closed the connection", exception),
			SshConnectionException => new ProtocolConnectException(ConnectFailure.ProtocolError, $"The SSH handshake with {endpoint} failed: {exception.Message}", exception),
			SshException => new ProtocolConnectException(ConnectFailure.ProtocolError, $"{endpoint}: {exception.Message}", exception),
			ArgumentException { ParamName: "username" } => new ProtocolConnectException(ConnectFailure.AuthenticationFailed, "A username is required.", exception),
			ArgumentException => new ProtocolConnectException(ConnectFailure.HostUnreachable, $"{endpoint} is not a valid address.", exception),
			_ => new ProtocolConnectException(ConnectFailure.Unknown, $"Could not connect to {endpoint}: {exception.Message}", exception),
		};
	}

	private static ProtocolConnectException Unreachable(string endpoint, string reason, Exception exception) =>
		new(ConnectFailure.HostUnreachable, $"Could not reach {endpoint}: {reason}.", exception);

	private static string AuthenticationMessage(Exception exception) =>
		$"The server rejected the login: {exception.Message.TrimEnd('.')}.";

	private static string Describe(SocketException exception) => exception.SocketErrorCode switch
	{
		SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain => "host not found",
		SocketError.ConnectionRefused => "connection refused",
		SocketError.NetworkUnreachable => "network unreachable",
		SocketError.HostUnreachable => "host unreachable",
		SocketError.ConnectionReset or SocketError.ConnectionAborted => "connection reset",
		SocketError.AddressNotAvailable => "address not available",
		_ => exception.Message.TrimEnd('.'),
	};
}
