using System.Net.Sockets;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Ssh.Connection;
using Mokaterm.Modules.Ssh.FileSystem;
using Renci.SshNet.Common;
using Renci.SshNet.Messages.Transport;
using Renci.SshNet.Sftp;

namespace Mokaterm.Modules.Ssh.Tests;

public sealed class ErrorMappingTests
{
	[Theory]
	[InlineData(SocketError.ConnectionRefused, "Could not reach web01:22: connection refused.")]
	[InlineData(SocketError.HostNotFound, "Could not reach web01:22: host not found.")]
	[InlineData(SocketError.NetworkUnreachable, "Could not reach web01:22: network unreachable.")]
	public void Connect_SocketErrors_AreUnreachable(SocketError error, string message)
	{
		ProtocolConnectException mapped = MapConnect(new SocketException((int)error));

		Assert.Equal(ConnectFailure.HostUnreachable, mapped.Failure);
		Assert.Equal(message, mapped.Message);
	}

	[Fact]
	public void Connect_SocketTimeout_IsTimeout() =>
		Assert.Equal(ConnectFailure.Timeout, MapConnect(new SocketException((int)SocketError.TimedOut)).Failure);

	[Fact]
	public void Connect_OperationTimeout_IsTimeout()
	{
		ProtocolConnectException mapped = MapConnect(new SshOperationTimeoutException("Session operation has timed out"));

		Assert.Equal(ConnectFailure.Timeout, mapped.Failure);
		Assert.Equal("web01:22 did not respond in time.", mapped.Message);
	}

	[Fact]
	public void Connect_AuthenticationFailure_KeepsTheServerReason()
	{
		ProtocolConnectException mapped = MapConnect(new SshAuthenticationException("Permission denied (publickey)."));

		Assert.Equal(ConnectFailure.AuthenticationFailed, mapped.Failure);
		Assert.Equal("The server rejected the login: Permission denied (publickey).", mapped.Message);
	}

	[Fact]
	public void Connect_HandshakeFailure_IsProtocolError()
	{
		ProtocolConnectException mapped = MapConnect(new SshConnectionException("No matching host key algorithm", DisconnectReason.KeyExchangeFailed));

		Assert.Equal(ConnectFailure.ProtocolError, mapped.Failure);
		Assert.Contains("No matching host key algorithm", mapped.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Connect_ConnectionDroppedDuringHandshake_IsUnreachable()
	{
		Assert.Equal(ConnectFailure.HostUnreachable, MapConnect(new SshConnectionException("aborted", DisconnectReason.ConnectionLost)).Failure);
		Assert.Equal(ConnectFailure.HostUnreachable, MapConnect(new SshConnectionException("reset", new SocketException((int)SocketError.ConnectionReset))).Failure);
	}

	[Fact]
	public void Connect_ProxyFailure_IsUnreachable() =>
		Assert.Equal(ConnectFailure.HostUnreachable, MapConnect(new ProxyException("SOCKS5: connection refused")).Failure);

	[Fact]
	public void Connect_MissingUsername_IsAuthenticationFailure() =>
		Assert.Equal(ConnectFailure.AuthenticationFailed, MapConnect(new ArgumentException("Value cannot be empty.", "username")).Failure);

	[Fact]
	public void Connect_CancellationAndMappedExceptions_PassThrough()
	{
		OperationCanceledException cancelled = new();
		ProtocolConnectException already = new(ConnectFailure.Cancelled, "cancelled");

		Assert.Same(cancelled, SshConnectErrors.ToConnectException(cancelled, "web01", 22));
		Assert.Same(already, SshConnectErrors.ToConnectException(already, "web01", 22));
	}

	[Fact]
	public void Connect_Unexpected_IsUnknown() =>
		Assert.Equal(ConnectFailure.Unknown, MapConnect(new InvalidOperationException("boom")).Failure);

	[Theory]
	[InlineData("web01", 22, "web01:22")]
	[InlineData("10.10.2.3", 2222, "10.10.2.3:2222")]
	[InlineData("::1", 22, "[::1]:22")]
	[InlineData("fe80::1%eth0", 22, "[fe80::1%eth0]:22")]
	public void FormatEndpoint_BracketsIpv6(string host, int port, string expected) =>
		Assert.Equal(expected, HostEndpoint.Format(host, port));

	[Fact]
	public void Files_MapSftpStatusCodes()
	{
		Assert.Equal(RemoteFileErrorKind.NotFound, MapFile(new SftpPathNotFoundException("No such file")));
		Assert.Equal(RemoteFileErrorKind.PermissionDenied, MapFile(new SftpPermissionDeniedException("Permission denied")));
		Assert.Equal(RemoteFileErrorKind.NotFound, MapFile(new SftpException(StatusCode.NoSuchFile)));
		Assert.Equal(RemoteFileErrorKind.NotSupported, MapFile(new SftpException(StatusCode.OperationUnsupported)));
		Assert.Equal(RemoteFileErrorKind.ConnectionLost, MapFile(new SftpException(StatusCode.ConnectionLost)));
		Assert.Equal(RemoteFileErrorKind.Unknown, MapFile(new SftpException(StatusCode.Failure, "Failure")));
	}

	[Fact]
	public void Files_MapConnectionFailures()
	{
		Assert.Equal(RemoteFileErrorKind.ConnectionLost, MapFile(new SshConnectionException("Client not connected.")));
		Assert.Equal(RemoteFileErrorKind.ConnectionLost, MapFile(new SshOperationTimeoutException()));
		Assert.Equal(RemoteFileErrorKind.ConnectionLost, MapFile(new ObjectDisposedException("SftpClient")));
		Assert.Equal(RemoteFileErrorKind.ConnectionLost, MapFile(new SocketException((int)SocketError.ConnectionReset)));
		Assert.Equal(RemoteFileErrorKind.NotSupported, MapFile(new NotSupportedException()));
	}

	[Fact]
	public void Files_KeepThePathAndCause()
	{
		SftpPathNotFoundException cause = new("No such file");

		Assert.True(RemoteFileErrors.TryMap(cause, "/etc/missing", out RemoteFileSystemException? mapped));
		Assert.Equal("/etc/missing", mapped.Path);
		Assert.Same(cause, mapped.InnerException);
		Assert.Equal("/etc/missing does not exist.", mapped.Message);
	}

	[Fact]
	public void Files_LeaveOtherExceptionsAlone()
	{
		Assert.False(RemoteFileErrors.TryMap(new OperationCanceledException(), "/x", out _));
		Assert.False(RemoteFileErrors.TryMap(new IOException("local disk full"), "/x", out _));
		Assert.False(RemoteFileErrors.TryMap(new RemoteFileSystemException(RemoteFileErrorKind.NotFound, "gone"), "/x", out _));
	}

	private static ProtocolConnectException MapConnect(Exception exception) =>
		Assert.IsType<ProtocolConnectException>(SshConnectErrors.ToConnectException(exception, "web01", 22));

	private static RemoteFileErrorKind MapFile(Exception exception)
	{
		Assert.True(RemoteFileErrors.TryMap(exception, "/srv/file", out RemoteFileSystemException? mapped));
		return mapped.Kind;
	}
}
