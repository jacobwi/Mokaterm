using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Mokaterm.Modules.Ssh.Tests.Fakes;

/// <summary>Answers one connection on a loopback port with a greeting, or accepts and says nothing.</summary>
internal sealed class GreetingServer : IDisposable
{
	private readonly TcpListener _listener;
	private int _accepted;

	public GreetingServer(string? greeting)
	{
		_listener = new TcpListener(IPAddress.Loopback, 0);
		_listener.Start();
		Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
		_ = AcceptAsync(greeting);
	}

	public int Port { get; }

	/// <summary>How many connections reached this port.</summary>
	public int Accepted => Volatile.Read(ref _accepted);

	public void Dispose() => _listener.Dispose();

	private async Task AcceptAsync(string? greeting)
	{
		try
		{
			using TcpClient client = await _listener.AcceptTcpClientAsync();
			_ = Interlocked.Increment(ref _accepted);
			if (greeting is not null)
			{
				byte[] bytes = Encoding.ASCII.GetBytes(greeting);
				await client.GetStream().WriteAsync(bytes);
				await client.GetStream().FlushAsync();
			}

			// Hold the connection open so a silent server stays silent instead of closing at once.
			await Task.Delay(TimeSpan.FromSeconds(5));
		}
		catch (Exception ex) when (ex is SocketException or ObjectDisposedException or IOException)
		{
			// The test finished with the listener.
		}
	}
}
