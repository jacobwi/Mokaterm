using System.Net.Sockets;
using System.Security.Authentication;
using FluentFTP.Exceptions;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Ftp.Connection;

namespace Mokaterm.Modules.Ftp.Tests;

public sealed class FtpConnectErrorsTests
{
	[Fact]
	public async Task DescribeAsync_RefusedAuthTls_IsProtocolError()
	{
		ProtocolConnectException exception = await DescribeAsync(new FtpSecurityNotAvailableException("AUTH TLS command failed."));

		Assert.Equal(ConnectFailure.ProtocolError, exception.Failure);
		Assert.Contains("does not offer FTPS", exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task DescribeAsync_RejectedLogin_IsAuthenticationFailed()
	{
		ProtocolConnectException exception = await DescribeAsync(new FtpAuthenticationException("530", "Login incorrect."));

		Assert.Equal(ConnectFailure.AuthenticationFailed, exception.Failure);
		Assert.Equal("The server rejected the login: Login incorrect.", exception.Message);
	}

	[Fact]
	public async Task DescribeAsync_TooManyConnections_IsProtocolErrorWithTheReply()
	{
		ProtocolConnectException exception = await DescribeAsync(new FtpCommandException("421", "Too many connections from this IP"));

		Assert.Equal(ConnectFailure.ProtocolError, exception.Failure);
		Assert.Contains("421 Too many connections from this IP", exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task DescribeAsync_TlsRequiredLogin_SuggestsExplicitTls()
	{
		ProtocolConnectException exception = await DescribeAsync(new FtpAuthenticationException("530", "Non-anonymous sessions must use encryption."));

		Assert.Equal(ConnectFailure.ProtocolError, exception.Failure);
		Assert.Contains("explicit TLS", exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task DescribeAsync_TlsHandshakeFailure_MentionsTheEncryptionMode()
	{
		ProtocolConnectException exception = await DescribeAsync(new AuthenticationException("Cannot determine the frame size or a corrupted frame was received."));

		Assert.Equal(ConnectFailure.ProtocolError, exception.Failure);
		Assert.Contains("990", exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task DescribeAsync_ReadTimeoutOnPort990_HintsAtImplicitTls()
	{
		ProtocolConnectException exception = await DescribeAsync(new TimeoutException(), port: 990);

		Assert.Equal(ConnectFailure.Timeout, exception.Failure);
		Assert.Contains("implicit TLS", exception.Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(SocketError.ConnectionRefused, ConnectFailure.HostUnreachable)]
	[InlineData(SocketError.HostNotFound, ConnectFailure.HostUnreachable)]
	[InlineData(SocketError.TimedOut, ConnectFailure.Timeout)]
	[InlineData(SocketError.ConnectionReset, ConnectFailure.ProtocolError)]
	[InlineData(SocketError.HostUnreachable, ConnectFailure.HostUnreachable)]
	public async Task DescribeAsync_SocketErrors_MapByCode(SocketError error, ConnectFailure expected) =>
		Assert.Equal(expected, (await DescribeAsync(new SocketException((int)error))).Failure);

	[Fact]
	public async Task DescribeAsync_ARefusedSocket_SaysToCheckThePortAndTheServer()
	{
		ProtocolConnectException exception = await DescribeAsync(new SocketException((int)SocketError.ConnectionRefused));

		Assert.Equal("127.0.0.1:21 refused the connection. Check the port and that the FTP server is running.", exception.Message);
	}

	[Fact]
	public async Task DescribeAsync_ATimedOutSocketOnPort990_KeepsFtpsOwnHint()
	{
		ProtocolConnectException exception = await DescribeAsync(new SocketException((int)SocketError.TimedOut), port: 990);

		Assert.Equal(ConnectFailure.Timeout, exception.Failure);
		Assert.Contains("implicit TLS", exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task DescribeAsync_AnIpv6Address_KeepsItsBracketsSoThePortIsReadable()
	{
		ProtocolConnectException exception = await DescribeAsync(new SocketException((int)SocketError.ConnectionRefused), host: "::1");

		Assert.StartsWith("[::1]:21 refused the connection.", exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task DescribeAsync_GenericConnectFailureThatUsedTheTimeout_IsTimeout()
	{
		ProtocolConnectException exception = await DescribeAsync(new IOException(FtpConnectErrors.FailedToConnectMessage), elapsed: TimeSpan.FromSeconds(16));

		Assert.Equal(ConnectFailure.Timeout, exception.Failure);
	}

	[Fact]
	public async Task DescribeAsync_GenericConnectFailureThatFailedFast_IsHostUnreachable()
	{
		ProtocolConnectException exception = await DescribeAsync(new IOException(FtpConnectErrors.FailedToConnectMessage), elapsed: TimeSpan.FromMilliseconds(20));

		Assert.Equal(ConnectFailure.HostUnreachable, exception.Failure);
		Assert.Contains("127.0.0.1:21", exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task DescribeAsync_GenericConnectFailureForUnknownHost_SaysSo()
	{
		ProtocolConnectException exception = await DescribeAsync(new IOException(FtpConnectErrors.FailedToConnectMessage), host: "no-such-host.invalid");

		Assert.Equal(ConnectFailure.HostUnreachable, exception.Failure);
		Assert.StartsWith("Could not find no-such-host.invalid", exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task DescribeAsync_ClosedGreeting_IsProtocolError()
	{
		ProtocolConnectException exception = await DescribeAsync(new IOException("The connection was terminated before a greeting could be read."));

		Assert.Equal(ConnectFailure.ProtocolError, exception.Failure);
	}

	[Fact]
	public async Task DescribeAsync_ExistingConnectException_IsKept()
	{
		ProtocolConnectException original = new(ConnectFailure.HostIdentityRejected, "Not trusted.");

		Assert.Same(original, await DescribeAsync(original));
	}

	private static Task<ProtocolConnectException> DescribeAsync(Exception exception, string host = "127.0.0.1", int port = 21, TimeSpan? elapsed = null)
	{
		FtpClientOptions options = FtpClientOptions.Create(host, port, FtpConnectionOptions.Default, new FtpSettings());
		return FtpConnectErrors.DescribeAsync(exception, options, elapsed ?? TimeSpan.Zero, TestContext.Current.CancellationToken);
	}
}
