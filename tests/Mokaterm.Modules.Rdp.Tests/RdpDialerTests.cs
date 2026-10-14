using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Mokaterm.Modules.Rdp.Protocol;

namespace Mokaterm.Modules.Rdp.Tests;

/// <summary>
/// The TCP dial. IronRDP's took the first address a name resolved to and waited on it without a limit, so a name whose
/// first address is dead never reached the one that works.
/// </summary>
public sealed class RdpDialerTests
{
	private static readonly TimeSpan Budget = TimeSpan.FromSeconds(10);

	// localhost resolves to ::1 first on Windows; nothing listens there, so the dial has to move on to 127.0.0.1.
	[Fact(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	public async Task ConnectAsync_ANameWithADeadFirstAddress_MovesOnToTheNext()
	{
		using TcpListener listener = new(IPAddress.Loopback, 0);
		listener.Start();
		int port = ((IPEndPoint)listener.LocalEndpoint).Port;
		ConcurrentQueue<IPAddress> tried = new();

		using Socket socket = await RdpDialer.ConnectAsync("localhost", port, Budget, tried.Enqueue, TestContext.Current.CancellationToken);

		Assert.True(socket.Connected);
		Assert.True(socket.NoDelay);
		Assert.Equal(IPAddress.Loopback, ((IPEndPoint)socket.RemoteEndPoint!).Address);
		Assert.Contains(IPAddress.Loopback, tried);
	}

	[Fact(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	public async Task ConnectAsync_AClosedPort_IsARefusal()
	{
		SocketException error = await Assert.ThrowsAsync<SocketException>(
			() => RdpDialer.ConnectAsync("127.0.0.1", LoopbackPort.Free(), Budget, null, TestContext.Current.CancellationToken));

		Assert.Equal(SocketError.ConnectionRefused, error.SocketErrorCode);
	}

	[Fact(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	public async Task ConnectAsync_ANameThatDoesNotResolve_SaysSo()
	{
		SocketException error = await Assert.ThrowsAsync<SocketException>(
			() => RdpDialer.ConnectAsync("mokaterm-no-such-host.invalid", 3389, Budget, null, TestContext.Current.CancellationToken));

		Assert.Equal(SocketError.HostNotFound, error.SocketErrorCode);
	}

	[Fact(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	public async Task ConnectAsync_AnAddressGivenNoTime_TimesOutRatherThanWaiting()
	{
		// TEST-NET-1 is never routed; with no time left the attempt has to end at once, whatever the network does.
		SocketException error = await Assert.ThrowsAsync<SocketException>(
			() => RdpDialer.ConnectAsync("192.0.2.1", 3389, TimeSpan.Zero, null, TestContext.Current.CancellationToken));

		Assert.Equal(SocketError.TimedOut, error.SocketErrorCode);
	}
}
