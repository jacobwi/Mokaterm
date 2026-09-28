using System.Net.Sockets;

namespace Mokaterm.Modules.Telnet.Protocol;

/// <summary>One open TCP connection to a telnet server: the socket and the stream the channel works on.</summary>
internal sealed class TelnetTransport : IAsyncDisposable
{
	private readonly TcpClient _client;
	private int _disposed;

	public TelnetTransport(TcpClient client, string host, int port)
	{
		ArgumentNullException.ThrowIfNull(client);
		_client = client;
		Stream = client.GetStream();
		Host = host;
		Port = port;
	}

	public Stream Stream { get; }

	public string Host { get; }

	public int Port { get; }

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		try
		{
			await Stream.DisposeAsync();
		}
		catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
		{
			// Closing a connection the other side already dropped fails again; it is gone either way.
		}

		_client.Dispose();
	}
}
