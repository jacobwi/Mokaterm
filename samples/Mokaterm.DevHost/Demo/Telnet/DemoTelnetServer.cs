using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace Mokaterm.DevHost.Demo.Telnet;

/// <summary>
/// A telnet server on a loopback port, so the module can be used without a device. It negotiates like a real
/// server, asks for the terminal type, follows the window size and runs a handful of commands behind a login.
/// </summary>
internal sealed class DemoTelnetServer : IAsyncDisposable
{
	private readonly TimeProvider _timeProvider;
	private readonly ILoggerFactory _loggerFactory;
	private readonly ILogger<DemoTelnetServer> _logger;
	private readonly TcpListener _listener;
	private readonly CancellationTokenSource _stopping = new();
	private Task _accepting = Task.CompletedTask;
	private int _disposed;

	public DemoTelnetServer(TimeProvider timeProvider, ILoggerFactory loggerFactory)
	{
		_timeProvider = timeProvider;
		_loggerFactory = loggerFactory;
		_logger = loggerFactory.CreateLogger<DemoTelnetServer>();
		_listener = new TcpListener(IPAddress.Loopback, 0);
		_listener.Start();
		Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
	}

	public int Port { get; }

	/// <summary>The name the seeded login types at the prompt.</summary>
	public string Username { get; } = "operator";

	/// <summary>Random for every run, and handed to the seeded login, which is why nobody ever types it.</summary>
	public string Password { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(9));

	public void Start()
	{
		_accepting = AcceptAsync(_stopping.Token);
		_logger.LogInformation("The demo telnet server listens on 127.0.0.1:{Port}", Port);
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		await _stopping.CancelAsync();
		_listener.Stop();
		try
		{
			await _accepting;
		}
		catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
		{
			// Stopping.
		}

		_stopping.Dispose();
	}

	private async Task AcceptAsync(CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			TcpClient client;
			try
			{
				client = await _listener.AcceptTcpClientAsync(cancellationToken);
			}
			catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
			{
				return;
			}

			_ = ServeAsync(client, cancellationToken);
		}
	}

	private async Task ServeAsync(TcpClient client, CancellationToken cancellationToken)
	{
		try
		{
			client.NoDelay = true;
			DemoTelnetConnection connection = new(
				client.GetStream(),
				Username,
				Password,
				_timeProvider,
				_loggerFactory.CreateLogger<DemoTelnetConnection>());
			await connection.RunAsync(cancellationToken);
		}
		catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
		{
			// A client that went away needs no handling.
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "The demo telnet connection failed.");
		}
		finally
		{
			client.Dispose();
		}
	}
}
