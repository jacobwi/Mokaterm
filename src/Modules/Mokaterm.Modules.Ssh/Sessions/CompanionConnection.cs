using Mokaterm.Modules.Ssh.Connection;
using Renci.SshNet;

namespace Mokaterm.Modules.Ssh.Sessions;

/// <summary>A second connection a session opens on first use and reopens after it dropped.</summary>
internal sealed class CompanionConnection<TClient> : IAsyncDisposable
	where TClient : BaseClient
{
	private readonly Func<CancellationToken, Task<TClient>> _connect;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private readonly CancellationTokenSource _disposal = new();
	private TClient? _client;
	private int _disposed;

	public CompanionConnection(Func<CancellationToken, Task<TClient>> connect) => _connect = connect;

	/// <summary>The client when it is open and connected, without connecting.</summary>
	public TClient? Current => Volatile.Read(ref _client) is { } client && ConnectionMonitor.IsConnected(client) ? client : null;

	/// <exception cref="ObjectDisposedException">The session closed.</exception>
	public async Task<TClient> GetAsync(CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
		using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposal.Token);
		await _gate.WaitAsync(linked.Token);
		try
		{
			if (Current is { } current)
			{
				return current;
			}

			if (Interlocked.Exchange(ref _client, null) is { } dropped)
			{
				await DisposeClientAsync(dropped);
			}

			TClient connected = await _connect(linked.Token);

			// Publish first, then look: DisposeAsync marks itself first, then takes the client. Both steps are full fences, so
			// a dispose racing this connect either finds the client or is seen here, and whoever takes it back disposes it.
			_ = Interlocked.Exchange(ref _client, connected);
			if (Volatile.Read(ref _disposed) != 0)
			{
				if (Interlocked.CompareExchange(ref _client, null, connected) == connected)
				{
					await DisposeClientAsync(connected);
				}

				throw new ObjectDisposedException(GetType().Name);
			}

			return connected;
		}
		finally
		{
			_gate.Release();
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		await _disposal.CancelAsync();
		if (Interlocked.Exchange(ref _client, null) is { } client)
		{
			await DisposeClientAsync(client);
		}
	}

	/// <summary>SSH.NET's Dispose sends a disconnect and waits for its message loop, so keep it off the caller's thread.</summary>
	internal static Task DisposeClientAsync(BaseClient client) => Task.Run(client.Dispose);
}
