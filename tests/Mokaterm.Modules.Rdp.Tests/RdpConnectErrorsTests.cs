using System.Net.Sockets;
using System.Security.Authentication;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Rdp.Protocol;

namespace Mokaterm.Modules.Rdp.Tests;

/// <summary>
/// What a failed RDP connect tells the user. <see cref="RdpConnectTests"/> covers which failure each exception maps to;
/// this covers the sentences, which is where the copies of the socket table drifted apart.
/// </summary>
public sealed class RdpConnectErrorsTests
{
	[Fact]
	public async Task DescribeAsync_ARefusedPort_SaysRemoteDesktopHasToBeTurnedOn()
	{
		ProtocolConnectException failure = await DescribeAsync(new SocketException((int)SocketError.ConnectionRefused));

		Assert.Equal(ConnectFailure.HostUnreachable, failure.Failure);
		Assert.Equal(
			"term01:3389 refused the connection. Remote Desktop listens on 3389 and has to be turned on for the account.",
			failure.Message);
	}

	[Fact]
	public async Task DescribeAsync_AnIpv6Address_KeepsItsBracketsSoThePortIsReadable()
	{
		ProtocolConnectException failure = await DescribeAsync(new SocketException((int)SocketError.ConnectionRefused), host: "2001:db8::5");

		Assert.StartsWith("[2001:db8::5]:3389 refused the connection.", failure.Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(SocketError.NetworkUnreachable)]
	[InlineData(SocketError.HostUnreachable)]
	public async Task DescribeAsync_NoRouteToTheDesktop_SaysSo(SocketError error)
	{
		ProtocolConnectException failure = await DescribeAsync(new SocketException((int)error));

		Assert.Equal(ConnectFailure.HostUnreachable, failure.Failure);
		Assert.Equal("There is no route to term01:3389.", failure.Message);
	}

	[Fact]
	public async Task DescribeAsync_AnUnknownHost_SaysToCheckTheName()
	{
		ProtocolConnectException failure = await DescribeAsync(new SocketException((int)SocketError.HostNotFound));

		Assert.Equal("Could not find term01. Check the host name.", failure.Message);
	}

	[Fact]
	public async Task DescribeAsync_ASocketFailureInsideAnIoException_IsReadThroughIt()
	{
		ProtocolConnectException failure = await DescribeAsync(new IOException("broken", new SocketException((int)SocketError.ConnectionReset)));

		Assert.Equal(ConnectFailure.ProtocolError, failure.Failure);
		Assert.Equal("term01:3389 closed the connection unexpectedly.", failure.Message);
	}

	[Fact]
	public async Task DescribeAsync_ATimedOutAttempt_NamesTheRdpConnectionAndNotJustTheSocket()
	{
		ProtocolConnectException failure = await DescribeAsync(new IOException("late"), timedOut: true);

		Assert.Equal(ConnectFailure.Timeout, failure.Failure);
		Assert.Equal("term01:3389 did not finish the RDP connection in time.", failure.Message);
	}

	[Fact]
	public async Task DescribeAsync_AFailedTlsHandshake_NamesTheEndpoint()
	{
		ProtocolConnectException failure = await DescribeAsync(new AuthenticationException("no cipher"));

		Assert.Equal(ConnectFailure.ProtocolError, failure.Failure);
		Assert.Equal("The TLS handshake with term01:3389 failed: no cipher", failure.Message);
	}

	[Fact]
	public async Task DescribeAsync_AServerThatStopsWhileConnecting_SaysSo()
	{
		ProtocolConnectException failure = await DescribeAsync(new EndOfStreamException());

		Assert.Equal(ConnectFailure.ProtocolError, failure.Failure);
		Assert.Equal("term01:3389 closed the connection while connecting.", failure.Message);
	}

	private static Task<ProtocolConnectException> DescribeAsync(Exception exception, string host = "term01", bool timedOut = false) =>
		RdpConnectErrors.DescribeAsync(exception, host, 3389, timedOut, TestContext.Current.CancellationToken);
}
