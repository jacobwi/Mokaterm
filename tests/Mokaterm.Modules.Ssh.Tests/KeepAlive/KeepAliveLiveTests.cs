using Mokaterm.Modules.Ssh.KeepAlive;
using Mokaterm.Modules.Ssh.Protocols;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace Mokaterm.Modules.Ssh.Tests.KeepAlive;

/// <summary>
/// Keepalives that want a reply against a real server named by <c>MOKATERM_TEST_SSH</c> (<c>user:password@host:port</c>):
/// the server answers them, and a remote forward started between them still gets its own answer.
/// </summary>
public sealed class KeepAliveLiveTests
{
	private const string ServerVariable = "MOKATERM_TEST_SSH";

	private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

	[Fact(Timeout = TestTimeouts.NetworkMilliseconds)]
	public async Task Ssh_AnswersKeepAlives_AndAForwardBetweenThemGetsItsOwnAnswer()
	{
		string value = LiveEndpoint.Require(ServerVariable, $"Set {ServerVariable}=user:password@host:port to run tests against a real SSH server.");
		LiveEndpoint server = LiveEndpoint.Parse(value, SshProtocolProvider.SshDescriptor.DefaultPort);
		(string user, string password) = server.RequireLogin(ServerVariable);
		CancellationToken token = TestContext.Current.CancellationToken;

		using SshClient client = new(server.Host, server.Port, user, password);
		await client.ConnectAsync(token).WaitAsync(TimeSpan.FromSeconds(30), token);

		GlobalRequestLine line = GlobalRequestLine.Open(client, TimeSpan.FromSeconds(15), TimeProvider.System, hold: null, out string? problem)
			?? throw new InvalidOperationException(problem);
		using (line)
		{
			await using KeepAliveMonitor monitor = new(line, new KeepAlivePolicy(Interval, 3), TimeProvider.System);

			// A few intervals: every keepalive is answered, success or failure, so the connection is judged and never lost.
			await Eventually.TrueAsync(() => line.State.Replies >= 3, "the server answered three keepalives", TimeSpan.FromSeconds(20));
			Assert.False(monitor.Lost.IsCompleted);
			Assert.False(line.State.Ignored);
			Assert.InRange(monitor.MissedReplies, 0, 1);

			// A remote forward started while keepalives keep going takes its own answer, not a keepalive's failure.
			ForwardedPortRemote port = new("127.0.0.1", (uint)Random.Shared.Next(20_000, 60_000), "127.0.0.1", 9);
			client.AddForwardedPort(port);
			try
			{
				await line.StartAsync(port.Start, token).WaitAsync(TimeSpan.FromSeconds(30), token);
			}
			catch (SshException ex) when (ex is not SshConnectionException)
			{
				Assert.Skip("The server refused the remote forward (AllowTcpForwarding, or the port is taken).");
			}

			Assert.True(port.IsStarted);
			await line.StopAsync(() => client.RemoveForwardedPort(port), sendsRequest: true).WaitAsync(TimeSpan.FromSeconds(30), token);
			port.Dispose();

			await Eventually.TrueAsync(() => line.State.Owed == 0, "every request was answered", TimeSpan.FromSeconds(20));
			long replies = line.State.Replies;
			await Eventually.TrueAsync(() => line.State.Replies > replies + 1, "keepalives go on after the forward", TimeSpan.FromSeconds(20));
			Assert.False(monitor.Lost.IsCompleted);
		}

		client.Disconnect();
	}
}
