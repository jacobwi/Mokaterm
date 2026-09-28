using System.Net;
using System.Net.Sockets;

namespace Mokaterm.Modules.Telnet.Tests.Loopback;

/// <summary>A listener on the loopback interface that hands each accepted client to the test as a peer it drives by hand.</summary>
internal sealed class LoopbackTelnetServer : IAsyncDisposable
{
	private readonly TcpListener _listener;
	private readonly List<LoopbackTelnetPeer> _peers = [];
	private int _disposed;

	public LoopbackTelnetServer()
	{
		_listener = new TcpListener(IPAddress.Loopback, 0);
		_listener.Start();
		Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
	}

	public int Port { get; }

	public async Task<LoopbackTelnetPeer> AcceptAsync(CancellationToken cancellationToken)
	{
		TcpClient client = await _listener.AcceptTcpClientAsync(cancellationToken);
		client.NoDelay = true;
		LoopbackTelnetPeer peer = new(client);
		_peers.Add(peer);
		return peer;
	}

	/// <summary>Stops listening, so a connect to this port is refused the way a closed port is.</summary>
	public void StopListening() => _listener.Stop();

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		_listener.Stop();
		foreach (LoopbackTelnetPeer peer in _peers)
		{
			await peer.DisposeAsync();
		}
	}
}
