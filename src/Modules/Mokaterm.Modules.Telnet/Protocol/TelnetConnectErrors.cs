using System.Net.Sockets;
using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Modules.Telnet.Protocol;

/// <summary>Turns connect failures into <see cref="ProtocolConnectException"/> with messages written for the user.</summary>
internal static class TelnetConnectErrors
{
	private static readonly SocketFailureHints Hints = new()
	{
		Refused = "Check the port, and that telnet is turned on: most systems ship with it off.",
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
			SocketException socket => await FromSocketErrorAsync(socket, host, endpoint, cancellationToken),
			IOException io when io.InnerException is SocketException inner => await FromSocketErrorAsync(inner, host, endpoint, cancellationToken),
			EndOfStreamException => new(ConnectFailure.ProtocolError, $"{endpoint} closed the connection while the options were being negotiated."),
			IOException io => new(ConnectFailure.ProtocolError, $"The connection to {endpoint} broke: {io.Message}", io),
			_ => new(ConnectFailure.Unknown, $"Could not connect to {endpoint}: {exception.Message}", exception),
		};
	}

	private static async Task<ProtocolConnectException> FromSocketErrorAsync(SocketException socket, string host, string endpoint, CancellationToken cancellationToken) =>
		(await SocketFailures.DescribeAsync(socket, host, endpoint, Hints, cancellationToken)).ToException(socket);
}
