using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Ssh.Connection;
using Mokaterm.Modules.Ssh.Tests.Fakes;

namespace Mokaterm.Modules.Ssh.Tests;

public sealed class PortGreetingTests
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Theory]
	[InlineData("RFB 003.008\n", "a VNC server")]
	[InlineData("220 ProFTPD Server ready\r\n", "an FTP server")]
	[InlineData("HTTP/1.1 400 Bad Request\r\n", "a web server")]
	public async Task ExplainAsync_NamesTheProtocolThePortGreetsWith(string greeting, string expected)
	{
		using GreetingServer server = new(greeting);
		ProtocolConnectException failure = new(ConnectFailure.HostUnreachable, "Could not reach the server: the server closed the connection.");

		ProtocolConnectException explained = await PortGreeting.ExplainAsync(failure, "127.0.0.1", server.Port, Ct);

		Assert.Contains(expected, explained.Message, StringComparison.Ordinal);
		Assert.StartsWith(failure.Message, explained.Message, StringComparison.Ordinal);
		Assert.Equal(failure.Failure, explained.Failure);
		Assert.Same(failure, explained.InnerException);
	}

	[Theory]
	[InlineData("SSH-2.0-OpenSSH_9.6\r\n")]
	[InlineData("  ")]
	public async Task ExplainAsync_KeepsTheFailure_WhenTheGreetingIsSshOrUnknown(string greeting)
	{
		using GreetingServer server = new(greeting);
		ProtocolConnectException failure = new(ConnectFailure.ProtocolError, "The SSH handshake failed.");

		Assert.Same(failure, await PortGreeting.ExplainAsync(failure, "127.0.0.1", server.Port, Ct));
	}

	[Fact]
	public async Task ExplainAsync_KeepsTheFailure_WhenThePortStaysSilent()
	{
		using GreetingServer server = new(greeting: null);
		ProtocolConnectException failure = new(ConnectFailure.HostUnreachable, "Could not reach the server.");

		Assert.Same(failure, await PortGreeting.ExplainAsync(failure, "127.0.0.1", server.Port, Ct));
	}

	[Fact]
	public async Task ExplainAsync_KeepsTheFailure_WhenNothingListens()
	{
		int port;
		using (GreetingServer closed = new(greeting: null))
		{
			port = closed.Port;
		}

		ProtocolConnectException failure = new(ConnectFailure.HostUnreachable, "Could not reach the server.");

		Assert.Same(failure, await PortGreeting.ExplainAsync(failure, "127.0.0.1", port, Ct));
	}

	[Theory]
	[InlineData(0)]
	[InlineData(70_000)]
	public async Task ExplainAsync_KeepsTheFailure_WhenThePortIsNotAPort(int port)
	{
		ProtocolConnectException failure = new(ConnectFailure.HostUnreachable, "127.0.0.1 is not a valid address.");

		Assert.Same(failure, await PortGreeting.ExplainAsync(failure, "127.0.0.1", port, Ct));
	}

	[Theory]
	[InlineData(ConnectFailure.HostUnreachable, true)]
	[InlineData(ConnectFailure.ProtocolError, true)]
	[InlineData(ConnectFailure.AuthenticationFailed, false)]
	[InlineData(ConnectFailure.Timeout, false)]
	[InlineData(ConnectFailure.Cancelled, false)]
	public void MayBeWrongProtocol_OnlyForFailuresThatCanMeanTheWrongPort(ConnectFailure failure, bool expected) =>
		Assert.Equal(expected, PortGreeting.MayBeWrongProtocol(failure));

	[Theory]
	[InlineData("SSH-2.0-OpenSSH_9.6\r\n", true)]
	[InlineData("RFB 003.008\n", false)]
	[InlineData(null, false)]
	public async Task AnswersSsh_OnlyForAnSshGreeting(string? greeting, bool expected)
	{
		using GreetingServer server = new(greeting);

		Assert.Equal(expected, await PortGreeting.AnswersSshAsync("127.0.0.1", server.Port, Ct));
	}

	[Fact]
	public async Task AnswersSsh_IsFalse_WhenNothingListens()
	{
		int port;
		using (GreetingServer closed = new(greeting: null))
		{
			port = closed.Port;
		}

		Assert.False(await PortGreeting.AnswersSshAsync("127.0.0.1", port, Ct));
	}

	[Fact]
	public void ReachesTargetDirectly_OnlyWithoutJumpHostsAndProxy()
	{
		Assert.True(new SshConnectionOptions().ReachesTargetDirectly);
		Assert.False(new SshConnectionOptions { JumpConnectionIds = [Guid.NewGuid()] }.ReachesTargetDirectly);
		Assert.False(new SshConnectionOptions { Proxy = new ProxyOptions { Kind = ProxyKind.Socks5, Host = "proxy", Port = 1080 } }.ReachesTargetDirectly);
	}
}
