using System.Net.Sockets;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Telnet.Protocol;

namespace Mokaterm.Modules.Telnet.Tests;

/// <summary>
/// What a failed telnet connect tells the user. The socket table is shared now, so what is asserted here is telnet's own
/// part: the hint that most systems ship with telnet off, the negotiation wording, and an address that names a port.
/// </summary>
public sealed class TelnetConnectErrorsTests
{
	[Fact]
	public async Task DescribeAsync_ARefusedPort_SaysTelnetIsUsuallyOff()
	{
		ProtocolConnectException failure = await DescribeAsync(new SocketException((int)SocketError.ConnectionRefused));

		Assert.Equal(ConnectFailure.HostUnreachable, failure.Failure);
		Assert.Equal(
			"switch:23 refused the connection. Check the port, and that telnet is turned on: most systems ship with it off.",
			failure.Message);
	}

	[Fact]
	public async Task DescribeAsync_AnIpv6Address_KeepsItsBracketsSoThePortIsReadable()
	{
		ProtocolConnectException failure = await DescribeAsync(new SocketException((int)SocketError.ConnectionRefused), host: "fe80::1");

		Assert.StartsWith("[fe80::1]:23 refused the connection.", failure.Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(SocketError.NetworkUnreachable)]
	[InlineData(SocketError.HostUnreachable)]
	public async Task DescribeAsync_NoRouteToTheDevice_SaysSo(SocketError error)
	{
		ProtocolConnectException failure = await DescribeAsync(new SocketException((int)error));

		Assert.Equal(ConnectFailure.HostUnreachable, failure.Failure);
		Assert.Equal("There is no route to switch:23.", failure.Message);
	}

	[Fact]
	public async Task DescribeAsync_AnUnknownHost_SaysToCheckTheName()
	{
		ProtocolConnectException failure = await DescribeAsync(new SocketException((int)SocketError.HostNotFound));

		Assert.Equal(ConnectFailure.HostUnreachable, failure.Failure);
		Assert.Equal("Could not find switch. Check the host name.", failure.Message);
	}

	[Fact]
	public async Task DescribeAsync_ASocketFailureInsideAnIoException_IsReadThroughIt()
	{
		ProtocolConnectException failure = await DescribeAsync(new IOException("broken", new SocketException((int)SocketError.ConnectionReset)));

		Assert.Equal(ConnectFailure.ProtocolError, failure.Failure);
		Assert.Equal("switch:23 closed the connection unexpectedly.", failure.Message);
	}

	[Fact]
	public async Task DescribeAsync_ADeviceThatStopsMidNegotiation_NamesTheOptions()
	{
		ProtocolConnectException failure = await DescribeAsync(new EndOfStreamException());

		Assert.Equal(ConnectFailure.ProtocolError, failure.Failure);
		Assert.Equal("switch:23 closed the connection while the options were being negotiated.", failure.Message);
	}

	[Fact]
	public async Task DescribeAsync_ATimedOutAttempt_IsATimeout()
	{
		ProtocolConnectException failure = await DescribeAsync(new IOException("late"), timedOut: true);

		Assert.Equal(ConnectFailure.Timeout, failure.Failure);
		Assert.Equal("switch:23 did not answer in time.", failure.Message);
	}

	[Fact]
	public async Task DescribeAsync_AnExistingConnectException_IsKept()
	{
		ProtocolConnectException original = new(ConnectFailure.Cancelled, "The login was cancelled.");

		Assert.Same(original, await DescribeAsync(original));
	}

	private static Task<ProtocolConnectException> DescribeAsync(Exception exception, string host = "switch", bool timedOut = false) =>
		TelnetConnectErrors.DescribeAsync(exception, host, 23, timedOut, TestContext.Current.CancellationToken);
}
