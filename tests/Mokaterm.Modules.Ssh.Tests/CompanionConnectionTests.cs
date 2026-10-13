using Mokaterm.Modules.Ssh.KeepAlive;
using Mokaterm.Modules.Ssh.Sessions;
using Renci.SshNet;

namespace Mokaterm.Modules.Ssh.Tests;

public sealed class CompanionConnectionTests
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task DisposeWhileConnecting_DisposesTheClientThatArrivesLate()
	{
		// The connect ignores cancellation, as SSH.NET does inside its key exchange.
		TaskCompletionSource<SshClient> connecting = new(TaskCreationOptions.RunContinuationsAsynchronously);
		CompanionConnection<SshClient> companion = new(_ => connecting.Task);
		Task<SshClient> opening = companion.GetAsync(Ct);

		await companion.DisposeAsync();
		SshClient late = CreateClient();
		connecting.SetResult(late);

		await Assert.ThrowsAsync<ObjectDisposedException>(() => opening);
		Assert.Throws<ObjectDisposedException>(() => late.IsConnected);
		Assert.Null(companion.Current);
	}

	[Fact]
	public async Task GetAsync_AfterDispose_Refuses()
	{
		CompanionConnection<SshClient> companion = new(_ => Task.FromResult(CreateClient()));

		await companion.DisposeAsync();

		await Assert.ThrowsAsync<ObjectDisposedException>(() => companion.GetAsync(Ct));
	}

	[Fact]
	public async Task AClientThatStopsAnswering_IsClosed_AndItsWatchGoesWithTheNextOne()
	{
		List<FakeWatch> watches = [];
		List<KeepAliveTimeoutException> reported = [];
		CompanionConnection<SshClient> companion = new(
			_ => Task.FromResult(CreateClient()),
			_ =>
			{
				FakeWatch watch = new();
				watches.Add(watch);
				return watch;
			},
			reported.Add);
		SshClient first = await companion.GetAsync(Ct);
		FakeWatch firstWatch = Assert.Single(watches);

		KeepAliveTimeoutException cause = KeepAliveTimeoutException.Missed(3);
		firstWatch.Lose(cause);

		// Its socket would still look open, so the companion closes it rather than let every use wait out a timeout.
		await Eventually.TrueAsync(() => IsDisposed(first), "the client that stopped answering was closed");
		Assert.Same(cause, Assert.Single(reported));

		SshClient second = await companion.GetAsync(Ct);
		Assert.NotSame(first, second);
		Assert.True(firstWatch.Disposed);
		Assert.Equal(2, watches.Count);

		await companion.DisposeAsync();
		Assert.True(watches[1].Disposed);
		Assert.True(IsDisposed(second));
	}

	[Fact]
	public async Task AWatchThatSaysNothing_LeavesTheClientAlone()
	{
		FakeWatch watch = new();
		CompanionConnection<SshClient> companion = new(_ => Task.FromResult(CreateClient()), _ => watch);
		SshClient client = await companion.GetAsync(Ct);

		await Eventually.StaysTrueAsync(() => !IsDisposed(client), "the client stays open");

		await companion.DisposeAsync();
		Assert.True(watch.Disposed);
	}

	// Never connected, which is all these tests need from a client.
	private static SshClient CreateClient() =>
		new(new ConnectionInfo("127.0.0.1", 22, "tester", new NoneAuthenticationMethod("tester")));

	private static bool IsDisposed(SshClient client)
	{
		try
		{
			_ = client.IsConnected;
			return false;
		}
		catch (ObjectDisposedException)
		{
			return true;
		}
	}

	private sealed class FakeWatch : IConnectionWatch
	{
		private readonly TaskCompletionSource<KeepAliveTimeoutException> _lost = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public Task<KeepAliveTimeoutException> Lost => _lost.Task;

		public bool Disposed { get; private set; }

		public void Lose(KeepAliveTimeoutException cause) => _lost.SetResult(cause);

		public ValueTask DisposeAsync()
		{
			Disposed = true;
			return ValueTask.CompletedTask;
		}
	}
}
