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

	// Never connected, which is all these tests need from a client.
	private static SshClient CreateClient() =>
		new(new ConnectionInfo("127.0.0.1", 22, "tester", new NoneAuthenticationMethod("tester")));
}
