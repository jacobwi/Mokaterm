using Mokaterm.Modules.Ssh.Connection;
using Mokaterm.Modules.Ssh.KeepAlive;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace Mokaterm.Modules.Ssh.Tests.KeepAlive;

/// <summary>How a session wires keepalives to its client, and how a verdict reaches the session.</summary>
public sealed class ConnectionKeepAliveTests
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task AVerdict_EndsTheSessionThroughTheConnectionMonitor()
	{
		using SshClient client = CreateClient();
		KeepAliveTimeoutException cause = KeepAliveTimeoutException.Missed(3);
		await using ConnectionMonitor monitor = new(client, Task.FromResult(cause));

		Assert.Same(cause, await monitor.Lost.WaitAsync(Eventually.Limit, Ct));
	}

	[Fact]
	public async Task WithoutASession_NothingIsJudged_AndForwardsRunAsSshNetRunsThem()
	{
		// A client that never connected has no session to reach, the same as an SSH.NET that moved it.
		using SshClient client = CreateClient();

		await using ConnectionKeepAlive on = ConnectionKeepAlive.Open(client, new KeepAlivePolicy(TimeSpan.FromSeconds(30), 3), TimeSpan.FromSeconds(15), TimeProvider.System);
		Assert.Null(on.Requests);
		Assert.Same(DirectForwardRequests.Instance, on.Forwards);
		Assert.False(string.IsNullOrWhiteSpace(on.Problem));
		Assert.False(on.Lost.IsCompleted);

		// Off is not a problem worth logging.
		await using ConnectionKeepAlive off = ConnectionKeepAlive.Open(client, null, TimeSpan.FromSeconds(15), TimeProvider.System);
		Assert.Null(off.Problem);
		Assert.False(off.Lost.IsCompleted);
	}

	[Fact]
	public void TheVerdict_IsAConnectionLoss_ThatNamesNoHost()
	{
		KeepAliveTimeoutException cause = KeepAliveTimeoutException.Missed(3);

		Assert.IsAssignableFrom<SshConnectionException>(cause);
		Assert.Equal("The server stopped answering. 3 keepalives in a row went unanswered.", cause.Message);
	}

	// Never connected, which is all these tests need from a client.
	private static SshClient CreateClient() =>
		new(new ConnectionInfo("127.0.0.1", 22, "tester", new NoneAuthenticationMethod("tester")));
}
