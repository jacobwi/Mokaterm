using System.Net.Sockets;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Vnc.Protocol;

namespace Mokaterm.Modules.Vnc.Tests;

/// <summary>
/// What a failed VNC connect tells the user. The socket table is shared now, so what is asserted here is the part that
/// is VNC's: the hint about display numbers, the handshake wording, and an address that names a port.
/// </summary>
public sealed class VncConnectErrorsTests
{
	[Fact]
	public async Task DescribeAsync_ARefusedPort_MentionsTheDisplayNumber()
	{
		ProtocolConnectException failure = await DescribeAsync(new SocketException((int)SocketError.ConnectionRefused));

		Assert.Equal(ConnectFailure.HostUnreachable, failure.Failure);
		Assert.Equal("desktop:5900 refused the connection. Check the port: display :1 listens on 5901, not 5900.", failure.Message);
	}

	[Fact]
	public async Task DescribeAsync_AnIpv6Address_KeepsItsBracketsSoThePortIsReadable()
	{
		ProtocolConnectException failure = await DescribeAsync(new SocketException((int)SocketError.ConnectionRefused), host: "::1");

		Assert.StartsWith("[::1]:5900 refused the connection.", failure.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task DescribeAsync_NoRouteToTheScreen_SaysSo()
	{
		ProtocolConnectException failure = await DescribeAsync(new SocketException((int)SocketError.HostUnreachable));

		Assert.Equal(ConnectFailure.HostUnreachable, failure.Failure);
		Assert.Equal("There is no route to desktop:5900.", failure.Message);
	}

	[Fact]
	public async Task DescribeAsync_AnUnknownHost_SaysToCheckTheName()
	{
		ProtocolConnectException failure = await DescribeAsync(new SocketException((int)SocketError.HostNotFound));

		Assert.Equal(ConnectFailure.HostUnreachable, failure.Failure);
		Assert.Equal("Could not find desktop. Check the host name.", failure.Message);
	}

	[Fact]
	public async Task DescribeAsync_ASocketFailureInsideAnIoException_IsReadThroughIt()
	{
		ProtocolConnectException failure = await DescribeAsync(new IOException("broken", new SocketException((int)SocketError.ConnectionReset)));

		Assert.Equal(ConnectFailure.ProtocolError, failure.Failure);
		Assert.Equal("desktop:5900 closed the connection unexpectedly.", failure.Message);
	}

	[Fact]
	public async Task DescribeAsync_AServerThatStopsMidHandshake_NamesTheHandshake()
	{
		ProtocolConnectException failure = await DescribeAsync(new EndOfStreamException());

		Assert.Equal(ConnectFailure.ProtocolError, failure.Failure);
		Assert.Equal("desktop:5900 closed the connection during the VNC handshake.", failure.Message);
	}

	[Fact]
	public async Task DescribeAsync_ATimedOutAttempt_IsATimeout()
	{
		ProtocolConnectException failure = await DescribeAsync(new IOException("late"), timedOut: true);

		Assert.Equal(ConnectFailure.Timeout, failure.Failure);
		Assert.Equal("desktop:5900 did not answer in time.", failure.Message);
	}

	[Fact]
	public async Task DescribeAsync_AProtocolFailure_KeepsItsOwnMessage()
	{
		ProtocolConnectException failure = await DescribeAsync(new VncProtocolException("The server offered no security type we speak."));

		Assert.Equal(ConnectFailure.ProtocolError, failure.Failure);
		Assert.Equal("The server offered no security type we speak.", failure.Message);
	}

	[Fact]
	public async Task DescribeAsync_AnExistingConnectException_IsKept()
	{
		ProtocolConnectException original = new(ConnectFailure.HostIdentityRejected, "Not trusted.");

		Assert.Same(original, await DescribeAsync(original));
	}

	private static Task<ProtocolConnectException> DescribeAsync(Exception exception, string host = "desktop", bool timedOut = false) =>
		VncConnectErrors.DescribeAsync(exception, host, 5900, timedOut, TestContext.Current.CancellationToken);
}
