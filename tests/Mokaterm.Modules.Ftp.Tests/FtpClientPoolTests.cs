using FluentFTP;
using FluentFTP.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Mokaterm.Modules.Ftp.FileSystem;

namespace Mokaterm.Modules.Ftp.Tests;

public sealed class FtpClientPoolTests
{
	private readonly FakeTimeProvider _time = new();
	private readonly AsyncFtpClient _sharedClient = new();
	private int _connects;
	private int _sharedLeases;
	private int _maxClients = 2;
	private Func<int, Exception?> _connectFailure = static _ => null;

	[Fact]
	public async Task Lease_ReusesAReleasedConnection()
	{
		await using FtpClientPool pool = CreatePool();
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;

		FtpClientLease first = await pool.LeaseAsync(cancellationToken);
		AsyncFtpClient client = first.Client;
		await first.ReleaseAsync(reusable: true);
		FtpClientLease second = await pool.LeaseAsync(cancellationToken);

		Assert.Same(client, second.Client);
		Assert.Equal(1, _connects);
		Assert.False(second.IsShared);
		await second.ReleaseAsync(reusable: true);
	}

	[Fact]
	public async Task Lease_AtTheLimit_WaitsForARelease()
	{
		_maxClients = 1;
		await using FtpClientPool pool = CreatePool();
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;

		FtpClientLease first = await pool.LeaseAsync(cancellationToken);
		Task<FtpClientLease> waiting = pool.LeaseAsync(cancellationToken).AsTask();
		await Task.Delay(50, cancellationToken);
		Assert.False(waiting.IsCompleted);

		await first.ReleaseAsync(reusable: true);
		FtpClientLease second = await waiting.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

		Assert.Same(first.Client, second.Client);
		Assert.Equal(1, _connects);
	}

	[Fact]
	public async Task Release_NotReusable_ClosesTheConnection()
	{
		await using FtpClientPool pool = CreatePool();
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;

		FtpClientLease first = await pool.LeaseAsync(cancellationToken);
		await first.ReleaseAsync(reusable: false);

		Assert.Equal(0, pool.OpenCount);
		FtpClientLease second = await pool.LeaseAsync(cancellationToken);
		Assert.NotSame(first.Client, second.Client);
		Assert.Equal(2, _connects);
	}

	[Fact]
	public async Task Release_Twice_OnlyCountsOnce()
	{
		await using FtpClientPool pool = CreatePool();

		FtpClientLease lease = await pool.LeaseAsync(TestContext.Current.CancellationToken);
		await lease.ReleaseAsync(reusable: false);
		await lease.ReleaseAsync(reusable: false);

		Assert.Equal(0, pool.OpenCount);
	}

	[Fact]
	public async Task Lease_WhenServerRefusesAnExtraConnection_WaitsForTheOpenOne()
	{
		_connectFailure = attempt => attempt == 2 ? new FtpCommandException("421", "Too many connections from this IP") : null;
		await using FtpClientPool pool = CreatePool();
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;

		FtpClientLease first = await pool.LeaseAsync(cancellationToken);
		Task<FtpClientLease> second = pool.LeaseAsync(cancellationToken).AsTask();
		await Task.Delay(50, cancellationToken);
		Assert.False(second.IsCompleted);

		await first.ReleaseAsync(reusable: true);

		Assert.Same(first.Client, (await second.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken)).Client);
		Assert.Equal(2, _connects);
		Assert.Equal(1, pool.OpenCount);
	}

	[Fact]
	public async Task Lease_WhenServerRefusesTheFirstExtraConnection_SharesTheBrowsingConnection()
	{
		_connectFailure = static _ => new FtpAuthenticationException("530", "Sorry, the maximum number of clients (1) from your host are already connected.");
		await using FtpClientPool pool = CreatePool();
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;

		FtpClientLease lease = await pool.LeaseAsync(cancellationToken);
		Assert.True(lease.IsShared);
		Assert.Same(_sharedClient, lease.Client);

		FtpClientLease again = await pool.LeaseAsync(cancellationToken);
		Assert.True(again.IsShared);
		Assert.Equal(1, _connects);
		Assert.Equal(2, _sharedLeases);
	}

	[Fact]
	public async Task Lease_AfterTheServerLimitExpires_TriesAnotherConnection()
	{
		_connectFailure = static attempt => attempt == 1 ? new FtpCommandException("421", "Too many connections") : null;
		await using FtpClientPool pool = CreatePool();
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;

		Assert.True((await pool.LeaseAsync(cancellationToken)).IsShared);
		_time.Advance(FtpClientPool.ServerLimitLifetime);
		FtpClientLease lease = await pool.LeaseAsync(cancellationToken);

		Assert.False(lease.IsShared);
		Assert.Equal(2, _connects);
	}

	[Fact]
	public async Task Lease_ConnectFailure_PropagatesAndFreesTheSlot()
	{
		_maxClients = 1;
		_connectFailure = static attempt => attempt == 1 ? new IOException("Failed to connect to host.") : null;
		await using FtpClientPool pool = CreatePool();
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;

		await Assert.ThrowsAsync<IOException>(() => pool.LeaseAsync(cancellationToken).AsTask());
		FtpClientLease lease = await pool.LeaseAsync(cancellationToken);

		Assert.False(lease.IsShared);
		Assert.Equal(1, pool.OpenCount);
	}

	[Fact]
	public async Task IdleConnections_CloseAfterAMinute()
	{
		await using FtpClientPool pool = CreatePool();
		FtpClientLease lease = await pool.LeaseAsync(TestContext.Current.CancellationToken);
		await lease.ReleaseAsync(reusable: true);

		_time.Advance(TimeSpan.FromSeconds(30));
		Assert.Equal(1, pool.IdleCount);

		_time.Advance(FtpClientPool.IdleLifetime);

		Assert.Equal(0, pool.IdleCount);
		Assert.Equal(0, pool.OpenCount);
	}

	[Fact]
	public async Task Lease_ConnectionIdleForAWhile_IsCheckedAndReplacedWhenDead()
	{
		await using FtpClientPool pool = CreatePool();
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		FtpClientLease first = await pool.LeaseAsync(cancellationToken);
		AsyncFtpClient stale = first.Client;
		await first.ReleaseAsync(reusable: true);

		_time.Advance(FtpClientPool.ProbeAfterIdle + TimeSpan.FromSeconds(1));
		FtpClientLease second = await pool.LeaseAsync(cancellationToken);

		// The test clients never connected, so the NOOP check finds this one dead.
		Assert.NotSame(stale, second.Client);
		Assert.True(stale.IsDisposed);
		Assert.Equal(2, _connects);
		Assert.Equal(1, pool.OpenCount);
	}

	[Fact]
	public async Task Dispose_AbortsConnectionsThatAreLentOut()
	{
		FtpClientPool pool = CreatePool();
		FtpClientLease held = await pool.LeaseAsync(TestContext.Current.CancellationToken);

		await pool.DisposeAsync();

		Assert.True(held.Client.IsDisposed);
	}

	[Fact]
	public async Task Release_AboveALoweredLimit_ClosesTheConnection()
	{
		await using FtpClientPool pool = CreatePool();
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		FtpClientLease first = await pool.LeaseAsync(cancellationToken);
		FtpClientLease second = await pool.LeaseAsync(cancellationToken);

		_maxClients = 1;
		await first.ReleaseAsync(reusable: true);
		await second.ReleaseAsync(reusable: true);

		Assert.Equal(1, pool.OpenCount);
		Assert.Equal(1, pool.IdleCount);
	}

	[Fact]
	public async Task Dispose_FailsWaitingAndLaterLeases()
	{
		_maxClients = 1;
		FtpClientPool pool = CreatePool();
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		FtpClientLease held = await pool.LeaseAsync(cancellationToken);
		Task<FtpClientLease> waiting = pool.LeaseAsync(cancellationToken).AsTask();

		await pool.DisposeAsync();

		await Assert.ThrowsAsync<ObjectDisposedException>(() => waiting);
		await Assert.ThrowsAsync<ObjectDisposedException>(() => pool.LeaseAsync(cancellationToken).AsTask());
		await held.ReleaseAsync(reusable: true);
		Assert.Equal(0, pool.OpenCount);
	}

	private FtpClientPool CreatePool() => new(ConnectAsync, () => _maxClients, LeaseSharedAsync, _time, NullLogger.Instance);

	private Task<AsyncFtpClient> ConnectAsync(CancellationToken cancellationToken)
	{
		int attempt = Interlocked.Increment(ref _connects);
		return _connectFailure(attempt) is { } failure ? Task.FromException<AsyncFtpClient>(failure) : Task.FromResult(new AsyncFtpClient());
	}

	private ValueTask<FtpClientLease> LeaseSharedAsync(CancellationToken cancellationToken)
	{
		Interlocked.Increment(ref _sharedLeases);
		return ValueTask.FromResult(new FtpClientLease(_sharedClient, isShared: true, static (_, _) => ValueTask.CompletedTask));
	}
}
