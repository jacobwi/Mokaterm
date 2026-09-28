using FluentFTP;
using FluentFTP.Exceptions;
using Microsoft.Extensions.Logging;
using Mokaterm.Modules.Ftp.Connection;

namespace Mokaterm.Modules.Ftp.FileSystem;

/// <summary>
/// Extra connections for transfers, so a download never blocks browsing: an FTP control connection runs one command at a
/// time. Connections open on demand up to a limit, idle ones close after a minute, and when the server refuses another
/// connection the pool shrinks to what it has, lending out the browsing connection if it has nothing at all.
/// </summary>
internal sealed class FtpClientPool : IAsyncDisposable
{
	internal static readonly TimeSpan IdleLifetime = TimeSpan.FromMinutes(1);

	/// <summary>A connection idle this long is checked with NOOP before it is lent out again.</summary>
	internal static readonly TimeSpan ProbeAfterIdle = TimeSpan.FromSeconds(15);

	// A server that refused a connection may take one later, after other sessions from this address have closed.
	internal static readonly TimeSpan ServerLimitLifetime = TimeSpan.FromMinutes(5);

	private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(15);

	private readonly Func<CancellationToken, Task<AsyncFtpClient>> _connectAsync;
	private readonly Func<int> _maxClients;
	private readonly Func<CancellationToken, ValueTask<FtpClientLease>> _leaseSharedAsync;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;
	private readonly Lock _gate = new();
	private readonly List<IdleClient> _idle = [];
	private readonly HashSet<AsyncFtpClient> _leased = [];
	private List<TaskCompletionSource> _waiters = [];
	private int _open;
	private int _serverLimit = int.MaxValue;
	private long _serverLimitSince;
	private ITimer? _sweepTimer;
	private bool _disposed;

	/// <param name="connectAsync">Opens and logs in a new connection.</param>
	/// <param name="maxClients">The configured limit, read on every lease so settings changes apply at once.</param>
	/// <param name="leaseSharedAsync">Lends out the browsing connection when the server allows no extra one.</param>
	public FtpClientPool(
		Func<CancellationToken, Task<AsyncFtpClient>> connectAsync,
		Func<int> maxClients,
		Func<CancellationToken, ValueTask<FtpClientLease>> leaseSharedAsync,
		TimeProvider timeProvider,
		ILogger logger)
	{
		_connectAsync = connectAsync;
		_maxClients = maxClients;
		_leaseSharedAsync = leaseSharedAsync;
		_timeProvider = timeProvider;
		_logger = logger;
	}

	/// <summary>Connections currently open, idle or lent out.</summary>
	public int OpenCount
	{
		get
		{
			lock (_gate)
			{
				return _open;
			}
		}
	}

	public int IdleCount
	{
		get
		{
			lock (_gate)
			{
				return _idle.Count;
			}
		}
	}

	public async ValueTask<FtpClientLease> LeaseAsync(CancellationToken cancellationToken)
	{
		while (true)
		{
			IdleClient? idle = null;
			Task? wait = null;
			bool connect = false;
			lock (_gate)
			{
				ObjectDisposedException.ThrowIf(_disposed, this);
				int limit = CurrentLimit();
				if (_idle.Count > 0)
				{
					idle = _idle[^1];
					_idle.RemoveAt(_idle.Count - 1);
					_leased.Add(idle.Value.Client);
				}
				else if (_open < limit)
				{
					_open++;
					connect = true;
				}
				else if (limit > 0)
				{
					TaskCompletionSource waiter = new(TaskCreationOptions.RunContinuationsAsynchronously);
					_waiters.Add(waiter);
					wait = waiter.Task;
				}
			}

			if (idle is { } reused)
			{
				if (await IsUsableAsync(reused, cancellationToken))
				{
					return CreateLease(reused.Client);
				}

				continue;
			}

			if (connect)
			{
				if (await TryConnectAsync(cancellationToken) is { } lease)
				{
					return lease;
				}

				continue;
			}

			if (wait is null)
			{
				return await _leaseSharedAsync(cancellationToken);
			}

			await wait.WaitAsync(cancellationToken);
		}
	}

	/// <summary>Closes idle connections and aborts the ones lent out, so no connection outlives the session.</summary>
	public async ValueTask DisposeAsync()
	{
		List<AsyncFtpClient> idle;
		List<AsyncFtpClient> leased;
		ITimer? timer;
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			idle = [.. _idle.Select(entry => entry.Client)];
			leased = [.. _leased];
			_open -= _idle.Count;
			_idle.Clear();
			timer = _sweepTimer;
			_sweepTimer = null;
			WakeWaiters();
		}

		if (timer is not null)
		{
			await timer.DisposeAsync();
		}

		// Together rather than one after another: each close may wait a moment for the server's goodbye.
		await Task.WhenAll(idle.Select(client => FtpClientFactory.CloseAsync(client).AsTask()));

		// The transfers using these fail with a lost connection; their leases still come back and are counted then.
		foreach (AsyncFtpClient client in leased)
		{
			await FtpClientFactory.DiscardAsync(client);
		}
	}

	/// <summary>Closes connections idle for longer than <see cref="IdleLifetime"/>. Runs on a timer.</summary>
	internal void CloseExpired()
	{
		List<AsyncFtpClient>? expired = null;
		lock (_gate)
		{
			// Released connections are appended, so the oldest idle one is always first.
			while (!_disposed && _idle.Count > 0 && _timeProvider.GetElapsedTime(_idle[0].IdleSince) >= IdleLifetime)
			{
				(expired ??= []).Add(_idle[0].Client);
				_idle.RemoveAt(0);
				_open--;
			}
		}

		if (expired is null)
		{
			return;
		}

		foreach (AsyncFtpClient client in expired)
		{
			_ = FtpClientFactory.CloseAsync(client).AsTask();
		}
	}

	private async Task<bool> IsUsableAsync(IdleClient idle, CancellationToken cancellationToken)
	{
		bool alive;
		try
		{
			alive = _timeProvider.GetElapsedTime(idle.IdleSince) < ProbeAfterIdle || await FtpClientFactory.IsAliveAsync(idle.Client, cancellationToken);
		}
		catch
		{
			await ReleaseAsync(idle.Client, reusable: false);
			throw;
		}

		if (!alive)
		{
			// The server dropped it while it sat idle; the caller connects a fresh one.
			await ReleaseAsync(idle.Client, reusable: false);
		}

		return alive;
	}

	private async Task<FtpClientLease?> TryConnectAsync(CancellationToken cancellationToken)
	{
		AsyncFtpClient client;
		try
		{
			client = await _connectAsync(cancellationToken);
		}
		catch (FtpCommandException ex) when (FtpErrors.IsConnectionLimit(ex))
		{
			int remaining;
			lock (_gate)
			{
				_open--;
				_serverLimit = _open;
				_serverLimitSince = _timeProvider.GetTimestamp();
				remaining = _open;
				WakeWaiters();
			}

			_logger.LogInformation("The FTP server refused another connection; transfers now share {Connections} extra connection(s)", remaining);
			return null;
		}
		catch
		{
			lock (_gate)
			{
				_open--;
				WakeWaiters();
			}

			throw;
		}

		bool disposed;
		lock (_gate)
		{
			disposed = _disposed;
			if (disposed)
			{
				_open--;
			}
			else
			{
				_leased.Add(client);
				_sweepTimer ??= _timeProvider.CreateTimer(_ => CloseExpired(), null, SweepInterval, SweepInterval);
			}
		}

		if (disposed)
		{
			await FtpClientFactory.DiscardAsync(client);
			throw new ObjectDisposedException(nameof(FtpClientPool));
		}

		return CreateLease(client);
	}

	private FtpClientLease CreateLease(AsyncFtpClient client) => new(client, isShared: false, (lease, reusable) => ReleaseAsync(lease.Client, reusable));

	private async ValueTask ReleaseAsync(AsyncFtpClient client, bool reusable)
	{
		bool keep;
		lock (_gate)
		{
			_leased.Remove(client);

			// A kept connection that the server drops meanwhile is caught by the NOOP check before its next use.
			keep = reusable && !_disposed && _open <= CurrentLimit();
			if (keep)
			{
				_idle.Add(new IdleClient(client, _timeProvider.GetTimestamp()));
			}
			else
			{
				_open--;
			}

			WakeWaiters();
		}

		if (!keep)
		{
			await FtpClientFactory.DiscardAsync(client);
		}
	}

	// Called with _gate held.
	private int CurrentLimit()
	{
		if (_serverLimit != int.MaxValue && _timeProvider.GetElapsedTime(_serverLimitSince) >= ServerLimitLifetime)
		{
			_serverLimit = int.MaxValue;
		}

		return Math.Min(Math.Max(1, _maxClients()), _serverLimit);
	}

	// Called with _gate held. Waiters re-check the pool, so waking all of them is enough; stale ones are harmless.
	private void WakeWaiters()
	{
		if (_waiters.Count == 0)
		{
			return;
		}

		List<TaskCompletionSource> waiters = _waiters;
		_waiters = [];
		foreach (TaskCompletionSource waiter in waiters)
		{
			waiter.TrySetResult();
		}
	}

	private readonly record struct IdleClient(AsyncFtpClient Client, long IdleSince);
}
