using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace Mokaterm.DevHost.Demo.Vnc;

/// <summary>
/// A VNC server on a loopback port, so the session view can be worked on without a real desktop. Its password is
/// generated for this run and handed to the seeded login, which is why nobody ever types it.
/// </summary>
internal sealed class DemoVncServer : IAsyncDisposable
{
	private readonly DemoScreen _screen;
	private readonly ILogger<DemoVncServer> _logger;
	private readonly ILoggerFactory _loggerFactory;
	private readonly TcpListener _listener;
	private readonly CancellationTokenSource _stopping = new();
	private Task _accepting = Task.CompletedTask;
	private int _disposed;

	public DemoVncServer(TimeProvider timeProvider, ILoggerFactory loggerFactory)
	{
		_screen = new DemoScreen(timeProvider);
		_loggerFactory = loggerFactory;
		_logger = loggerFactory.CreateLogger<DemoVncServer>();
		_listener = new TcpListener(IPAddress.Loopback, 0);
		_listener.Start();
		Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
	}

	public int Port { get; }

	/// <summary>Random for every run. VNC authentication only uses the first eight bytes, so it is exactly that long.</summary>
	public string Password { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(6));

	public void Start()
	{
		_screen.Start();
		_accepting = AcceptAsync(_stopping.Token);
		_logger.LogInformation("The demo VNC server listens on 127.0.0.1:{Port}", Port);
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

		await _screen.DisposeAsync();
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
		await using DemoVncClient viewer = new(client, _screen, Password, _loggerFactory.CreateLogger<DemoVncClient>());
		try
		{
			await viewer.RunAsync(cancellationToken);
		}
		catch (Exception ex) when (ex is OperationCanceledException or EndOfStreamException or IOException or SocketException or ObjectDisposedException)
		{
			// The viewer closed its connection.
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "A demo VNC viewer ended with an error.");
		}
	}
}
