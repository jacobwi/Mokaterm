using System.Net;
using System.Net.Sockets;

namespace Mokaterm.Tests.Shared;

internal static class LoopbackPort
{
	/// <summary>
	/// Binding and letting go hands out a port nothing is listening on, which is what a refused-connection test needs,
	/// and a free port for a test server to open next.
	/// </summary>
	public static int Free()
	{
		using Socket socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
		socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
		return ((IPEndPoint)socket.LocalEndPoint!).Port;
	}
}
