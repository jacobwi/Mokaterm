using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Mokaterm.Modules.Ssh.Tests.Fakes;
using Mokaterm.Modules.Ssh.Tunnels;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace Mokaterm.Modules.Ssh.Tests;

/// <summary>
/// The tunnel lifecycle against a client that is not connected, which is how a real failure to open reaches the
/// manager: the session has to stay alive and the reason has to land on the tunnel.
/// </summary>
public sealed class SshTunnelManagerTests
{
	[Fact]
	public async Task StartSaved_KeepsTheSessionWhenEveryTunnelFails()
	{
		using SshClient client = CreateClient();
		RecordingInteraction interaction = new();
		await using SshTunnelManager manager = new(client, Guid.NewGuid(), interaction, NullLogger.Instance);

		await manager.StartSavedAsync([Local(8080), Local(8081)], TestContext.Current.CancellationToken);

		Assert.Equal(2, manager.Tunnels.Count);
		Assert.All(manager.Tunnels, status => Assert.Equal(SshTunnelState.Failed, status.State));
		Assert.All(manager.Tunnels, status => Assert.False(string.IsNullOrWhiteSpace(status.Error)));
		Assert.Equal(0, manager.OpenCount);
		Assert.Equal("2 tunnels could not open", Assert.Single(interaction.Notices).Title);
	}

	[Fact]
	public async Task StartSaved_LeavesManualTunnelsClosedAndSaysNothing()
	{
		using SshClient client = CreateClient();
		RecordingInteraction interaction = new();
		await using SshTunnelManager manager = new(client, Guid.NewGuid(), interaction, NullLogger.Instance);

		await manager.StartSavedAsync([Local(8080) with { OpenWithSession = false }], TestContext.Current.CancellationToken);

		SshTunnelStatus status = Assert.Single(manager.Tunnels);
		Assert.Equal(SshTunnelState.Closed, status.State);
		Assert.Null(status.Error);
		Assert.False(status.IsSessionOnly);
		Assert.Empty(interaction.Notices);
	}

	[Fact]
	public async Task Add_MarksTheTunnelAsBelongingToThisSessionOnly()
	{
		using SshClient client = CreateClient();
		await using SshTunnelManager manager = new(client, Guid.NewGuid(), null, NullLogger.Instance);

		SshTunnelStatus status = await manager.AddAsync(Local(9000), TestContext.Current.CancellationToken);

		Assert.True(status.IsSessionOnly);
		Assert.Equal(SshTunnelState.Failed, status.State);
		Assert.Same(status.Tunnel, Assert.Single(manager.Tunnels).Tunnel);
	}

	[Fact]
	public async Task Add_RefusesATunnelThatCouldNotOpenAnyway()
	{
		using SshClient client = CreateClient();
		await using SshTunnelManager manager = new(client, Guid.NewGuid(), null, NullLogger.Instance);

		await Assert.ThrowsAsync<ArgumentException>(
			() => manager.AddAsync(Local(9000) with { DestinationHost = "" }, TestContext.Current.CancellationToken));
		Assert.Empty(manager.Tunnels);
	}

	[Fact]
	public async Task Close_AndOpen_MoveTheStateBackAndForth()
	{
		using SshClient client = CreateClient();
		await using SshTunnelManager manager = new(client, Guid.NewGuid(), null, NullLogger.Instance);
		SshTunnelDefinition tunnel = Local(9100) with { OpenWithSession = false };
		await manager.StartSavedAsync([tunnel], TestContext.Current.CancellationToken);

		Assert.Equal(SshTunnelState.Failed, (await manager.OpenAsync(tunnel.Id, TestContext.Current.CancellationToken)).State);
		Assert.Equal(SshTunnelState.Closed, (await manager.CloseAsync(tunnel.Id, TestContext.Current.CancellationToken)).State);
	}

	[Fact]
	public async Task Remove_ForgetsTheTunnel()
	{
		using SshClient client = CreateClient();
		await using SshTunnelManager manager = new(client, Guid.NewGuid(), null, NullLogger.Instance);
		SshTunnelStatus added = await manager.AddAsync(Local(9200), TestContext.Current.CancellationToken);

		await manager.RemoveAsync(added.Tunnel.Id, TestContext.Current.CancellationToken);

		Assert.Empty(manager.Tunnels);
	}

	[Fact]
	public async Task Changed_FiresWhileTunnelsAreWorkedOn()
	{
		using SshClient client = CreateClient();
		await using SshTunnelManager manager = new(client, Guid.NewGuid(), null, NullLogger.Instance);
		int changes = 0;
		manager.Changed += () => Interlocked.Increment(ref changes);

		await manager.AddAsync(Local(9300), TestContext.Current.CancellationToken);

		Assert.True(changes > 0, "Adding a tunnel should have raised Changed.");
	}

	[Fact]
	public async Task Changed_AHandlerThatThrows_DoesNotBreakTheManagerOrTheOtherHandlers()
	{
		using SshClient client = CreateClient();
		await using SshTunnelManager manager = new(client, Guid.NewGuid(), null, NullLogger.Instance);
		int changes = 0;
		manager.Changed += () => throw new InvalidOperationException("The view is gone.");
		manager.Changed += () => Interlocked.Increment(ref changes);

		SshTunnelStatus status = await manager.AddAsync(Local(9500), TestContext.Current.CancellationToken);

		Assert.Equal(SshTunnelState.Failed, status.State);
		Assert.True(Volatile.Read(ref changes) > 0, "The handler after the broken one still hears about the change.");
	}

	[Fact]
	public async Task UnknownTunnel_IsAnError()
	{
		using SshClient client = CreateClient();
		await using SshTunnelManager manager = new(client, Guid.NewGuid(), null, NullLogger.Instance);

		await Assert.ThrowsAsync<KeyNotFoundException>(() => manager.OpenAsync(Guid.NewGuid(), TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task Dispose_ClosesEverythingAndRefusesMoreWork()
	{
		using SshClient client = CreateClient();
		SshTunnelManager manager = new(client, Guid.NewGuid(), null, NullLogger.Instance);
		await manager.StartSavedAsync([Local(9400)], TestContext.Current.CancellationToken);

		await manager.DisposeAsync();

		Assert.Empty(manager.Tunnels);
		await Assert.ThrowsAsync<ObjectDisposedException>(() => manager.OpenAsync(Guid.NewGuid(), TestContext.Current.CancellationToken));
	}

	[Theory]
	[InlineData(SocketError.AddressAlreadyInUse, "Another program already listens on 127.0.0.1:8080.")]
	[InlineData(SocketError.AccessDenied, "This account may not listen on 127.0.0.1:8080.")]
	[InlineData(SocketError.AddressNotAvailable, "127.0.0.1 is not an address of this machine.")]
	public void Describe_TurnsSocketFailuresIntoSomethingActionable(SocketError error, string expected) =>
		Assert.Equal(expected, SshTunnelManager.Describe(new SocketException((int)error), Local(8080)));

	[Fact]
	public void Describe_ExplainsARefusedServerBind()
	{
		string message = SshTunnelManager.Describe(new SshException("Port forwarding request was rejected"), Remote(2222));

		Assert.Contains("GatewayPorts", message, StringComparison.Ordinal);
	}

	[Fact]
	public void Describe_SaysWhenTheConnectionIsGone() =>
		Assert.Equal("The SSH connection is closed.", SshTunnelManager.Describe(new SshConnectionException("Client not connected."), Local(8080)));

	// Never connected: AddForwardedPort throws, which is the same path a dropped session takes.
	private static SshClient CreateClient() =>
		new(new ConnectionInfo("127.0.0.1", 22, "tester", new NoneAuthenticationMethod("tester")));

	private static SshTunnelDefinition Local(int listenPort) => SshTunnelDefinition.Create(SshTunnelKind.Local) with
	{
		ListenPort = listenPort,
		DestinationHost = "db.internal",
		DestinationPort = 5432,
	};

	private static SshTunnelDefinition Remote(int listenPort) => SshTunnelDefinition.Create(SshTunnelKind.Remote) with
	{
		ListenPort = listenPort,
		DestinationHost = "127.0.0.1",
		DestinationPort = 22,
	};
}
