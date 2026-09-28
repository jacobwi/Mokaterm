using System.Net.Sockets;
using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Modules.Vnc.Protocol;

/// <summary>Turns connect failures into <see cref="ProtocolConnectException"/> with messages written for the user.</summary>
internal static class VncConnectErrors
{
	private static readonly SocketFailureHints Hints = new()
	{
		Refused = "Check the port: display :1 listens on 5901, not 5900.",
	};

	public static async Task<ProtocolConnectException> DescribeAsync(Exception exception, string host, int port, bool timedOut, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(exception);
		string endpoint = HostEndpoint.Format(host, port);
		if (timedOut)
		{
			return new ProtocolConnectException(ConnectFailure.Timeout, $"{endpoint} did not answer in time.", exception);
		}

		return exception switch
		{
			ProtocolConnectException connect => connect,
			VncProtocolException protocol => new(ConnectFailure.ProtocolError, protocol.Message, protocol),
			SocketException socket => await FromSocketErrorAsync(socket, host, endpoint, cancellationToken),
			EndOfStreamException => new(ConnectFailure.ProtocolError, $"{endpoint} closed the connection during the VNC handshake."),
			IOException io when io.InnerException is SocketException inner => await FromSocketErrorAsync(inner, host, endpoint, cancellationToken),
			IOException io => new(ConnectFailure.ProtocolError, $"The connection to {endpoint} broke: {io.Message}", io),
			_ => new(ConnectFailure.Unknown, $"Could not connect to {endpoint}: {exception.Message}", exception),
		};
	}

	private static async Task<ProtocolConnectException> FromSocketErrorAsync(SocketException socket, string host, string endpoint, CancellationToken cancellationToken) =>
		(await SocketFailures.DescribeAsync(socket, host, endpoint, Hints, cancellationToken)).ToException(socket);
}
