using System.Net;
using System.Net.Sockets;

namespace Mokaterm.Abstractions.Protocols;

/// <summary>
/// What a failed TCP dial means, for every protocol that dials one. Five modules wrote this table out and the copies
/// drifted, so the decision and the wording live here: a refused port reads the same over VNC as it does over MQTT, and
/// a branch added here reaches every protocol at once.
/// </summary>
public static class SocketFailures
{
	/// <summary>The sentence for a host name that nothing resolves.</summary>
	public static string UnknownHost(string host) => $"Could not find {host}. Check the host name.";

	/// <summary>
	/// What to tell the user about <paramref name="socket"/>. <paramref name="endpoint"/> is what the message calls the
	/// server, normally <see cref="HostEndpoint.Format"/>, and it may say more than a host and a port (an MQTT
	/// WebSocket address).
	/// </summary>
	public static async Task<SocketFailureReport> DescribeAsync(
		SocketException socket,
		string host,
		string endpoint,
		SocketFailureHints hints,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(socket);
		ArgumentNullException.ThrowIfNull(hints);
		switch (socket.SocketErrorCode)
		{
			case SocketError.HostNotFound:
			case SocketError.NoData:
			case SocketError.TryAgain:
				return new SocketFailureReport(ConnectFailure.HostUnreachable, UnknownHost(host));
			case SocketError.TimedOut:
				return new SocketFailureReport(ConnectFailure.Timeout, $"{endpoint} did not answer in time.");
			case SocketError.ConnectionRefused:
				return new SocketFailureReport(
					ConnectFailure.HostUnreachable,
					With($"{endpoint} refused the connection.", hints.Refused));
			case SocketError.NetworkUnreachable:
			case SocketError.HostUnreachable:
				return new SocketFailureReport(ConnectFailure.HostUnreachable, $"There is no route to {endpoint}.");
			case SocketError.ConnectionReset:
			case SocketError.ConnectionAborted:
			case SocketError.Shutdown:
				return new SocketFailureReport(
					ConnectFailure.ProtocolError,
					With($"{endpoint} closed the connection unexpectedly.", hints.Reset));
			default:
				return await IsUnknownHostAsync(host, cancellationToken)
					? new SocketFailureReport(ConnectFailure.HostUnreachable, UnknownHost(host))
					: new SocketFailureReport(ConnectFailure.HostUnreachable, $"Could not connect to {endpoint}: {socket.Message}");
		}
	}

	/// <summary>
	/// True when <paramref name="host"/> is a name that does not resolve. A name that cannot be looked up reaches a
	/// caller as a plain connect failure on some systems, so it is checked before the message settles for "could not
	/// connect".
	/// </summary>
	public static async Task<bool> IsUnknownHostAsync(string host, CancellationToken cancellationToken = default)
	{
		if (IPAddress.TryParse(host, out _))
		{
			return false;
		}

		try
		{
			await Dns.GetHostAddressesAsync(host, cancellationToken);
			return false;
		}
		catch (Exception ex) when (ex is SocketException or ArgumentException)
		{
			return true;
		}
	}

	private static string With(string message, string? hint) => string.IsNullOrEmpty(hint) ? message : message + " " + hint;
}
