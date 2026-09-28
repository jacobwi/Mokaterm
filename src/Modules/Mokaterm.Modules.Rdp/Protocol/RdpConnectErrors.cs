using System.Net.Sockets;
using System.Security.Authentication;
using Devolutions.IronRdp;
using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Modules.Rdp.Protocol;

/// <summary>Turns connect failures into <see cref="ProtocolConnectException"/> with messages written for the user.</summary>
internal static class RdpConnectErrors
{
	private static readonly SocketFailureHints Hints = new()
	{
		Refused = "Remote Desktop listens on 3389 and has to be turned on for the account.",
	};

	/// <summary>
	/// Words a server puts in a CredSSP failure when the account is the problem. IronRDP passes the operating
	/// system's own text through, and only some of it arrives as a typed error kind.
	/// </summary>
	private static readonly string[] LoginWords =
	[
		"logon",
		"log on",
		"password",
		"credential",
		"authentication failed",
		"access denied",
		"account",
		"unknown user",
	];

	/// <summary>True when the failure is about the login rather than the connection, so another password may work.</summary>
	public static bool IsAuthenticationFailure(Exception exception)
	{
		ArgumentNullException.ThrowIfNull(exception);
		if (exception is RdpAuthenticationException)
		{
			return true;
		}

		if (exception is not IronRdpException ironRdp)
		{
			return false;
		}

		if (ironRdp.Inner.GetKind() is IronRdpErrorKind.CredsspError or IronRdpErrorKind.AccessDenied)
		{
			return true;
		}

		string text = Describe(ironRdp);
		return LoginWords.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase));
	}

	public static async Task<ProtocolConnectException> DescribeAsync(Exception exception, string host, int port, bool timedOut, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(exception);
		string endpoint = HostEndpoint.Format(host, port);
		if (timedOut || exception is TimeoutException)
		{
			return new ProtocolConnectException(
				ConnectFailure.Timeout,
				$"{endpoint} did not finish the RDP connection in time.",
				exception);
		}

		return exception switch
		{
			ProtocolConnectException connect => connect,
			RdpAuthenticationException auth => new(ConnectFailure.AuthenticationFailed, auth.Message, auth),
			IronRdpException ironRdp => FromIronRdp(ironRdp, endpoint),
			IronRdpLibException lib => FromLibrary(lib, host, endpoint),
			AuthenticationException tls => new(ConnectFailure.ProtocolError, $"The TLS handshake with {endpoint} failed: {tls.Message}", tls),
			SocketException socket => await FromSocketErrorAsync(socket, host, endpoint, cancellationToken),
			EndOfStreamException => new(ConnectFailure.ProtocolError, $"{endpoint} closed the connection while connecting."),
			IOException io when io.InnerException is SocketException inner => await FromSocketErrorAsync(inner, host, endpoint, cancellationToken),
			IOException io => new(ConnectFailure.ProtocolError, $"The connection to {endpoint} broke: {io.Message}", io),
			_ => new(ConnectFailure.Unknown, $"Could not connect to {endpoint}: {exception.Message}", exception),
		};
	}

	private static string Describe(IronRdpException exception)
	{
		try
		{
			return exception.Inner.ToDisplay();
		}
		catch (ObjectDisposedException)
		{
			return exception.Message;
		}
	}

	private static ProtocolConnectException FromIronRdp(IronRdpException exception, string endpoint)
	{
		string detail = Describe(exception);
		if (IsAuthenticationFailure(exception))
		{
			return new ProtocolConnectException(
				ConnectFailure.AuthenticationFailed,
				$"{endpoint} refused the login: {detail}",
				exception);
		}

		return exception.Inner.GetKind() switch
		{
			IronRdpErrorKind.DecodeError or IronRdpErrorKind.PduError or IronRdpErrorKind.EncodeError => new(
				ConnectFailure.ProtocolError,
				$"{endpoint} answered with something that is not RDP ({detail}). Check the port.",
				exception),
			IronRdpErrorKind.IO => new(ConnectFailure.ProtocolError, $"The connection to {endpoint} broke: {detail}", exception),
			IronRdpErrorKind.WrongOS => new(ConnectFailure.Unknown, $"This part of RDP does not run on this operating system: {detail}", exception),
			_ => new(ConnectFailure.ProtocolError, $"Could not open an RDP session on {endpoint}: {detail}", exception),
		};
	}

	private static ProtocolConnectException FromLibrary(IronRdpLibException exception, string host, string endpoint) =>
		exception.ErrorType switch
		{
			IronRdpLibExceptionType.CannotResolveDns => new(ConnectFailure.HostUnreachable, SocketFailures.UnknownHost(host), exception),
			IronRdpLibExceptionType.ConnectionFailed => new(ConnectFailure.HostUnreachable, $"Could not connect to {endpoint}: {exception.Message}", exception),
			IronRdpLibExceptionType.EndOfFile => new(ConnectFailure.ProtocolError, $"{endpoint} closed the connection while connecting.", exception),
			_ => new(ConnectFailure.Unknown, $"Could not connect to {endpoint}: {exception.Message}", exception),
		};

	private static async Task<ProtocolConnectException> FromSocketErrorAsync(SocketException socket, string host, string endpoint, CancellationToken cancellationToken) =>
		(await SocketFailures.DescribeAsync(socket, host, endpoint, Hints, cancellationToken)).ToException(socket);
}
