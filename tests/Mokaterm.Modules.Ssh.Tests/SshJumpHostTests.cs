using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Ssh.Connection;
using Mokaterm.Modules.Ssh.Protocols;
using Mokaterm.Modules.Ssh.Tests.Fakes;

namespace Mokaterm.Modules.Ssh.Tests;

/// <summary>
/// The jump chain up to the point where a real server would be needed: what is resolved, in what order, and what the
/// user is told when a hop is missing, loops back or cannot be reached. The full two-hop path needs live servers and
/// lives in <see cref="SshIntegrationTests"/>.
/// </summary>
public sealed class SshJumpHostTests
{
	[Fact]
	public async Task Connect_ResolvesEveryHopBeforeDiallingTheFirst()
	{
		int closedPort = LoopbackPort.Free();
		FakeConnectionResolver resolver = new();
		Guid first = resolver.Add("127.0.0.1", closedPort);
		Guid second = resolver.Add("127.0.0.1", closedPort);

		ProtocolConnectException failure = await Assert.ThrowsAsync<ProtocolConnectException>(
			() => ConnectAsync(resolver, [first, second]));

		// The first hop is dialled directly and nothing listens there, so the chain never reaches the second.
		Assert.Equal([first, second], resolver.Resolved);
		Assert.Equal(ConnectFailure.HostUnreachable, failure.Failure);
	}

	[Fact]
	public async Task Connect_SaysWhichJumpHostIsGone()
	{
		FakeConnectionResolver resolver = new();

		ProtocolConnectException failure = await Assert.ThrowsAsync<ProtocolConnectException>(
			() => ConnectAsync(resolver, [Guid.NewGuid()]));

		Assert.Equal(ConnectFailure.AuthenticationFailed, failure.Failure);
		Assert.Contains("no longer exists", failure.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Connect_RefusesAChainThatPointsBackAtItself()
	{
		FakeConnectionResolver resolver = new();
		Guid hop = resolver.Add("127.0.0.1", 22);
		resolver.SetJumpHosts(hop, hop);

		ProtocolConnectException failure = await Assert.ThrowsAsync<ProtocolConnectException>(
			() => ConnectAsync(resolver, [hop]));

		Assert.Equal(ConnectFailure.ProtocolError, failure.Failure);
		Assert.Contains("lead back", failure.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Connect_ReportsAJumpHostThatCannotBeReached()
	{
		int closedPort = LoopbackPort.Free();
		FakeConnectionResolver resolver = new();
		Guid hop = resolver.Add("127.0.0.1", closedPort);

		ProtocolConnectException failure = await Assert.ThrowsAsync<ProtocolConnectException>(
			() => ConnectAsync(resolver, [hop]));

		Assert.Equal(ConnectFailure.HostUnreachable, failure.Failure);
		Assert.Contains("connection refused", failure.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task Connect_DoesNotProbeTheTargetDirectly_WhenAJumpHostFails()
	{
		// The target's address answers like a web server when dialled from here, which says nothing about the port behind
		// the jump host, and the failure is the jump host's anyway.
		using GreetingServer target = new("HTTP/1.1 400 Bad Request\r\n");
		FakeConnectionResolver resolver = new();
		Guid hop = resolver.Add("127.0.0.1", LoopbackPort.Free());

		ProtocolConnectException failure = await Assert.ThrowsAsync<ProtocolConnectException>(
			() => ConnectAsync(resolver, [hop], "127.0.0.1", target.Port));

		Assert.Equal(ConnectFailure.HostUnreachable, failure.Failure);
		Assert.DoesNotContain("answers like", failure.Message, StringComparison.Ordinal);
		Assert.Equal(0, target.Accepted);
	}

	[Fact]
	public async Task Connect_WithoutAResolverSaysWhyItCannot()
	{
		ProtocolConnectException failure = await Assert.ThrowsAsync<ProtocolConnectException>(
			() => ConnectAsync(resolver: null, [Guid.NewGuid()]));

		Assert.Equal(ConnectFailure.ProtocolError, failure.Failure);
		Assert.Contains("saved logins", failure.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Trail_StopsACycleAndCapsTheChain()
	{
		SshJumpTrail trail = new();
		Guid connectionId = Guid.NewGuid();

		Assert.True(trail.TryEnter(connectionId));
		Assert.False(trail.TryEnter(connectionId));

		SshJumpTrail deep = new();
		for (int hop = 0; hop < SshJumpTrail.MaxHops; hop++)
		{
			Assert.True(deep.TryEnter(Guid.NewGuid()));
		}

		Assert.Equal(SshJumpTrail.MaxHops, deep.Depth);
		Assert.False(deep.TryEnter(Guid.NewGuid()));
	}

	private static Task<IProtocolSession> ConnectAsync(IConnectionResolver? resolver, IReadOnlyList<Guid> jumpHosts, string targetAddress = "target.internal", int targetPort = 22)
	{
		Guid hostId = Guid.NewGuid();
		HostProfile host = new() { Id = hostId, Address = targetAddress };
		ConnectionProfile connection = new()
		{
			Id = Guid.NewGuid(),
			HostId = hostId,
			ProtocolId = "ssh",
			Username = "tester",
			AuthenticationMethod = AuthenticationMethod.Password,
			Options = ProtocolOptions.Empty.With(SshConnectionOptions.JumpHostsKey, string.Join(',', jumpHosts.Select(id => id.ToString("D")))),
		};

		ProtocolConnectContext context = new()
		{
			SessionId = Guid.NewGuid(),
			Host = host,
			Connection = connection,
			Port = targetPort,
			Credentials = new PasswordCredentialSource("tester", "secret"),
			HostVerifier = FakeHostVerifier.Accepting(),
			Interaction = new DismissingInteraction(),
			Resolver = resolver,
		};

		SshConnector connector = new(new FakeSettingsService());
		return new SshProtocolProvider(connector).ConnectAsync(context, TestContext.Current.CancellationToken);
	}
}
