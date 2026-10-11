using System.Net;
using System.Net.Sockets;
using System.Text;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Modules.Ssh.Agent;
using Mokaterm.Modules.Ssh.Connection;
using Mokaterm.Modules.Ssh.Forwarding;
using Mokaterm.Modules.Ssh.Protocols;
using Mokaterm.Modules.Ssh.Tests.Fakes;

namespace Mokaterm.Modules.Ssh.Tests;

/// <summary>
/// Agent and X11 forwarding against a real server named by <c>MOKATERM_TEST_SSH</c> (<c>user:password@host:port</c>).
/// The agent at this end is a fake on a pipe of the test's own, named in the SSH settings so it is found before any agent
/// of the machine running the test, and the X server is a listener of the test's own.
/// </summary>
public sealed class SshForwardingLiveTests
{
	private const string ServerVariable = "MOKATERM_TEST_SSH";

	[Fact(Timeout = 120_000)]
	public async Task Ssh_ForwardsTheAgentAndX11()
	{
		string value = LiveEndpoint.Require(ServerVariable, $"Set {ServerVariable}=user:password@host:port to run tests against a real SSH server.");
		LiveEndpoint server = LiveEndpoint.Parse(value, SshProtocolProvider.SshDescriptor.DefaultPort);
		(string user, string password) = server.RequireLogin(ServerVariable);
		CancellationToken token = TestContext.Current.CancellationToken;

		byte[] blob = Convert.FromBase64String(TestKeys.Ed25519PublicKey);
		await using FakeAgentServer agent = new(request => request is [SshAgentClient.RequestIdentities]
			? FakeAgentServer.IdentitiesAnswer((blob, "mokaterm-live"))
			: FakeAgentServer.Failure());
		using TcpListener xServer = new(IPAddress.Loopback, 0);
		xServer.Start();
		int display = ((IPEndPoint)xServer.LocalEndpoint).Port - X11Display.FirstPort;

		FakeSettingsService settings = new();
		settings.Set(new SshSettings { AgentEndpoint = agent.Address.Display });
		ProtocolOptions options = new SshConnectionOptions
		{
			Forwarding = new SshForwardingOptions { ForwardAgent = true, ForwardX11 = true, Display = $"127.0.0.1:{display}" },
		}.ApplyTo(ProtocolOptions.Empty);

		HostProfile host = new() { Id = Guid.NewGuid(), Address = server.Host };
		ProtocolConnectContext context = new()
		{
			SessionId = Guid.NewGuid(),
			Host = host,
			Connection = new ConnectionProfile
			{
				Id = Guid.NewGuid(),
				HostId = host.Id,
				ProtocolId = SshProtocolIds.Ssh,
				Port = server.Port,
				Username = user,
				AuthenticationMethod = AuthenticationMethod.Password,
				Options = options,
			},
			Port = server.Port,
			Credentials = new PasswordCredentialSource(user, password),
			HostVerifier = FakeHostVerifier.Accepting(),
			Interaction = new DismissingInteraction(),
		};

		IProtocolSession session = await new SshProtocolProvider(new SshConnector(settings)).ConnectAsync(context, token);
		await using (session)
		{
			ITerminalChannel terminal = session.GetFeature<ITerminalChannel>() ?? throw new InvalidOperationException("No terminal.");

			// ssh-add on the server talks to the agent through the forwarded channel.
			await terminal.WriteAsync(Encoding.UTF8.GetBytes("ssh-add -l; echo agent-$((6*7))\n"), token);
			string listed = await ReadUntilAsync(terminal, "agent-42", token);
			Assert.Contains(TestKeys.Ed25519Fingerprint, listed, StringComparison.Ordinal);
			Assert.Contains([SshAgentClient.RequestIdentities], agent.Requests);

			await terminal.WriteAsync(Encoding.UTF8.GetBytes("echo \"display=[$DISPLAY]\"; echo shown-$((6*7))\n"), token);
			string shown = await ReadUntilAsync(terminal, "shown-42", token);
			if (shown.Contains("display=[]", StringComparison.Ordinal))
			{
				Assert.Skip("The server does not forward X11 (X11Forwarding no, or no xauth there).");
			}

			// A TCP connection to the server's display port is what an X client makes first; sshd turns it into an x11
			// channel, and this end connects to its X server as soon as that channel opens.
			await terminal.WriteAsync(Encoding.UTF8.GetBytes("d=${DISPLAY#*:}; d=${d%%.*}; (exec 3<>/dev/tcp/127.0.0.1/$((6000 + d))); echo x11-$((6*7))\n"), token);
			using TcpClient accepted = await xServer.AcceptTcpClientAsync(token).AsTask().WaitAsync(TimeSpan.FromSeconds(30), token);
			Assert.True(accepted.Connected);
			_ = await ReadUntilAsync(terminal, "x11-42", token);
		}

		await session.Completion;
	}

	private static async Task<string> ReadUntilAsync(ITerminalChannel terminal, string expected, CancellationToken token)
	{
		StringBuilder output = new();
		byte[] buffer = new byte[4096];
		using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(token);
		limit.CancelAfter(TimeSpan.FromSeconds(30));
		while (!output.ToString().Contains(expected, StringComparison.Ordinal))
		{
			int read = await terminal.ReadAsync(buffer, limit.Token);
			if (read == 0)
			{
				break;
			}

			output.Append(Encoding.UTF8.GetString(buffer, 0, read));
		}

		return output.ToString();
	}
}
