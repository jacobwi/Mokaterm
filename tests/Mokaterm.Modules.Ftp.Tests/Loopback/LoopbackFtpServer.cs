using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Mokaterm.Modules.Ftp.Tests.Loopback;

/// <summary>A small FTP and FTPS server on 127.0.0.1 that speaks enough of the protocol for FluentFTP.</summary>
internal sealed class LoopbackFtpServer : IAsyncDisposable
{
	private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
	private readonly CancellationTokenSource _stopping = new();
	private readonly ConcurrentDictionary<int, LoopbackFtpConnection> _connections = new();
	private readonly Task _acceptLoop;
	private int _totalConnections;

	public LoopbackFtpServer(LoopbackFtpServerOptions options)
	{
		Options = options;
		_listener.Start();
		Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
		_acceptLoop = AcceptLoopAsync();
	}

	public LoopbackFtpServerOptions Options { get; }

	public FakeFileTree Files => Options.Files;

	public int Port { get; }

	public int OpenConnections => _connections.Count;

	public int TotalConnections => Volatile.Read(ref _totalConnections);

	/// <summary>Every command received, with PASS arguments masked.</summary>
	public ConcurrentQueue<string> Commands { get; } = new();

	/// <summary>Drops every open connection but keeps listening, like a server's idle timeout.</summary>
	public void DropConnections()
	{
		foreach (LoopbackFtpConnection connection in _connections.Values)
		{
			connection.Abort();
		}
	}

	/// <summary>Stops accepting connections and drops the open ones, like a server that went away.</summary>
	public async Task StopAsync()
	{
		await _stopping.CancelAsync();
		_listener.Stop();
		foreach (LoopbackFtpConnection connection in _connections.Values)
		{
			connection.Abort();
		}

		try
		{
			await _acceptLoop;
		}
		catch (Exception)
		{
			// The listener throws when it stops; that is the point.
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (!_stopping.IsCancellationRequested)
		{
			await StopAsync();
		}

		_stopping.Dispose();
	}

	private async Task AcceptLoopAsync()
	{
		while (!_stopping.IsCancellationRequested)
		{
			TcpClient client;
			try
			{
				client = await _listener.AcceptTcpClientAsync(_stopping.Token);
			}
			catch (Exception) when (_stopping.IsCancellationRequested)
			{
				return;
			}

			int id = Interlocked.Increment(ref _totalConnections);
			LoopbackFtpConnection connection = new(this, client);
			_connections[id] = connection;
			_ = RunAsync(id, connection);
		}
	}

	private async Task RunAsync(int id, LoopbackFtpConnection connection)
	{
		try
		{
			await connection.RunAsync(_stopping.Token);
		}
		catch (Exception)
		{
			// Clients hang up in the middle of things; the next connection starts clean.
		}
		finally
		{
			_connections.TryRemove(id, out _);
			await connection.DisposeAsync();
		}
	}
}
