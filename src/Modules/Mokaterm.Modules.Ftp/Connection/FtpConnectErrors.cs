using System.Net.Sockets;
using System.Security.Authentication;
using FluentFTP.Exceptions;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Ftp.FileSystem;

namespace Mokaterm.Modules.Ftp.Connection;

/// <summary>Turns connect failures into <see cref="ProtocolConnectException"/> with messages written for the user.</summary>
internal static class FtpConnectErrors
{
	// FluentFTP reports every failed TCP connect (unknown host, refused, timed out) with this message and no inner exception.
	internal const string FailedToConnectMessage = "Failed to connect to host.";

	private static readonly SocketFailureHints Hints = new()
	{
		Refused = "Check the port and that the FTP server is running.",
	};

	public static async Task<ProtocolConnectException> DescribeAsync(Exception exception, FtpClientOptions options, TimeSpan elapsed, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(exception);
		ArgumentNullException.ThrowIfNull(options);
		string endpoint = HostEndpoint.Format(options.Host, options.Port);
		return exception switch
		{
			ProtocolConnectException connect => connect,
			FtpSecurityNotAvailableException =>
				new(ConnectFailure.ProtocolError, $"{endpoint} does not offer FTPS: it refused AUTH TLS. Choose another encryption mode or check the server.", exception),
			FtpCommandException command when FtpErrors.IsTlsRequired(command) =>
				new(ConnectFailure.ProtocolError, $"{endpoint} only accepts encrypted logins. Choose explicit TLS in the connection's FTP options.", exception),
			FtpCommandException command when FtpErrors.IsConnectionLimit(command) =>
				new(ConnectFailure.ProtocolError, $"{endpoint} refused the connection: {FtpErrors.DescribeReply(command)}", exception),
			FtpAuthenticationException authentication => new(ConnectFailure.AuthenticationFailed, DescribeLoginFailure(authentication), exception),
			FtpCommandException command => new(ConnectFailure.ProtocolError, $"{endpoint} refused the connection: {FtpErrors.DescribeReply(command)}", exception),
			AuthenticationException or FtpInvalidCertificateException =>
				new(ConnectFailure.ProtocolError, $"The TLS handshake with {endpoint} failed. Check the encryption mode: implicit FTPS usually uses port 990, explicit FTPS port 21.", exception),
			TimeoutException => TimedOut(endpoint, options, exception),
			SocketException socket => await FromSocketErrorAsync(socket, endpoint, options, exception, cancellationToken),
			FtpMissingSocketException => new(ConnectFailure.HostUnreachable, $"Could not connect to {endpoint}.", exception),
			IOException when exception.Message == FailedToConnectMessage => await DiagnoseAsync(options, endpoint, elapsed, exception, cancellationToken),
			IOException => new(ConnectFailure.ProtocolError, $"{endpoint} closed the connection: {exception.Message}", exception),
			FtpException => new(ConnectFailure.ProtocolError, $"FTP error from {endpoint}: {exception.Message}", exception),
			_ => new(ConnectFailure.Unknown, $"Could not connect to {endpoint}: {exception.Message}", exception),
		};
	}

	/// <summary>The reason shown with the next password prompt.</summary>
	public static string DescribeLoginFailure(FtpCommandException exception)
	{
		ArgumentNullException.ThrowIfNull(exception);
		string message = FtpErrors.ReplyText(exception).TrimEnd('.');
		return message.Length == 0 ? "The server rejected the user name or password." : $"The server rejected the login: {message}.";
	}

	private static async Task<ProtocolConnectException> DiagnoseAsync(FtpClientOptions options, string endpoint, TimeSpan elapsed, Exception exception, CancellationToken cancellationToken)
	{
		if (await SocketFailures.IsUnknownHostAsync(options.Host, cancellationToken))
		{
			return new ProtocolConnectException(ConnectFailure.HostUnreachable, SocketFailures.UnknownHost(options.Host), exception);
		}

		// A refused or unroutable connection fails at once; one that used up the whole timeout never got an answer.
		return elapsed >= options.ConnectTimeout
			? TimedOut(endpoint, options, exception)
			: new ProtocolConnectException(ConnectFailure.HostUnreachable, $"Could not connect to {endpoint}. The server refused the connection or cannot be reached.", exception);
	}

	private static async Task<ProtocolConnectException> FromSocketErrorAsync(
		SocketException socket,
		string endpoint,
		FtpClientOptions options,
		Exception exception,
		CancellationToken cancellationToken)
	{
		// A timed out dial keeps FTP's own sentence: its hint about port 990 is read from this connection's options.
		if (socket.SocketErrorCode == SocketError.TimedOut)
		{
			return TimedOut(endpoint, options, exception);
		}

		// FluentFTP is the one that failed, so it stays the cause rather than the socket exception it wrapped.
		return (await SocketFailures.DescribeAsync(socket, options.Host, endpoint, Hints, cancellationToken)).ToException(exception);
	}

	private static ProtocolConnectException TimedOut(string endpoint, FtpClientOptions options, Exception exception)
	{
		// An implicit FTPS server waits for TLS before it greets, so an unencrypted client just sees silence.
		string hint = options.Encryption != FtpEncryption.Implicit && options.Port == FtpConnectionOptions.ImplicitTlsPort
			? " Port 990 is normally implicit FTPS; try implicit TLS."
			: "";
		return new ProtocolConnectException(ConnectFailure.Timeout, $"{endpoint} did not respond in time.{hint}", exception);
	}
}
