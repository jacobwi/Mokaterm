using Mokaterm.Modules.Ssh.Connection;
using Mokaterm.Modules.Ssh.KeepAlive;
using Renci.SshNet;

namespace Mokaterm.Modules.Ssh.Sessions;

/// <summary>
/// A second connection a session opens on first use and reopens after it dropped. It is a TCP connection of its own,
/// which can go quiet while the session's stays busy (a NAT entry that expired, say), so it is judged on its own, by the
/// same rule as the session: once its server has answered and then leaves its keepalives unanswered it is closed, and the
/// next use opens a fresh one.
/// </summary>
internal sealed class CompanionConnection<TClient> : IAsyncDisposable
	where TClient : BaseClient
{
	private readonly Func<CancellationToken, Task<TClient>> _connect;
	private readonly Func<TClient, IConnectionWatch?>? _watch;
	private readonly Action<KeepAliveTimeoutException>? _stoppedAnswering;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private readonly CancellationTokenSource _disposal = new();
	private TClient? _client;
	private IConnectionWatch? _watching;
	private int _disposed;

	/// <param name="watch">Starts what judges a newly opened client, such as its keepalive.</param>
	/// <param name="stoppedAnswering">Hears about a client that was closed for not answering, for the log.</param>
	public CompanionConnection(
		Func<CancellationToken, Task<TClient>> connect,
		Func<TClient, IConnectionWatch?>? watch = null,
		Action<KeepAliveTimeoutException>? stoppedAnswering = null)
	{
		_connect = connect;
		_watch = watch;
		_stoppedAnswering = stoppedAnswering;
	}

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

			if (Interlocked.Exchange(ref _watching, null) is { } stale)
			{
				await stale.DisposeAsync();
			}

			if (Interlocked.Exchange(ref _client, null) is { } dropped)
			{
				await DisposeClientAsync(dropped);
			}

			TClient connected = await _connect(linked.Token);

			// Publish first, then look: DisposeAsync marks itself first, then takes the watch and the client. Every step is a
			// full fence, so a dispose racing this connect either finds them or is seen here, and whoever takes one back
			// disposes it.
			IConnectionWatch? watching = _watch?.Invoke(connected);
			_ = Interlocked.Exchange(ref _watching, watching);
			_ = Interlocked.Exchange(ref _client, connected);
			if (Volatile.Read(ref _disposed) != 0)
			{
				if (Interlocked.CompareExchange(ref _watching, null, watching) == watching && watching is not null)
				{
					await watching.DisposeAsync();
				}

				if (Interlocked.CompareExchange(ref _client, null, connected) == connected)
				{
					await DisposeClientAsync(connected);
				}

				throw new ObjectDisposedException(GetType().Name);
			}

			if (watching is not null)
			{
				_ = DropWhenLostAsync(connected, watching.Lost);
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
		if (Interlocked.Exchange(ref _watching, null) is { } watching)
		{
			await watching.DisposeAsync();
		}

		if (Interlocked.Exchange(ref _client, null) is { } client)
		{
			await DisposeClientAsync(client);
		}
	}

	/// <summary>SSH.NET's Dispose sends a disconnect and waits for its message loop, so keep it off the caller's thread.</summary>
	internal static Task DisposeClientAsync(BaseClient client) => Task.Run(client.Dispose);

	/// <summary>
	/// Closes <paramref name="client"/> once it is judged lost, unless it was replaced or closed meanwhile. Its socket still
	/// looks open, so without this every use would wait out its own timeout on a connection nobody answers.
	/// </summary>
	private async Task DropWhenLostAsync(TClient client, Task<KeepAliveTimeoutException> lost)
	{
		KeepAliveTimeoutException cause = await lost;
		if (Interlocked.CompareExchange(ref _client, null, client) != client)
		{
			return;
		}

		try
		{
			_stoppedAnswering?.Invoke(cause);
			await DisposeClientAsync(client);
		}
		catch (Exception)
		{
			// Nobody waits for this: a client that fails to close on a dead connection is gone all the same.
		}
	}
}
