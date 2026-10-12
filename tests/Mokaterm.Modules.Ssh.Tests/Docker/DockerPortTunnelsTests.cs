using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.Modules.Ssh.Tunnels;

namespace Mokaterm.Modules.Ssh.Tests.Docker;

/// <summary>The tunnel Open goes through: one added per destination, reused while it is open, reopened when it failed.</summary>
public sealed class DockerPortTunnelsTests
{
	private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

	[Fact]
	public async Task NothingOpen_AddsASessionOnlyTunnelFromAFreeLoopbackPort()
	{
		RecordingTunnels tunnels = new();

		SshTunnelStatus status = await OpenAsync(tunnels, "127.0.0.1", 8080, "shop-web-1 port 80");

		SshTunnelDefinition added = Assert.Single(tunnels.Added);
		Assert.Equal(SshTunnelKind.Local, added.Kind);
		Assert.Equal("127.0.0.1", added.ListenHost);
		Assert.Equal(0, added.ListenPort);
		Assert.Equal("127.0.0.1", added.DestinationHost);
		Assert.Equal(8080, added.DestinationPort);
		Assert.Equal("shop-web-1 port 80", added.Name);
		Assert.Equal(SshTunnelState.Open, status.State);
		Assert.NotNull(status.BoundPort);
	}

	[Fact]
	public async Task OpeningTheSamePortAgain_UsesTheTunnelAlreadyOpen()
	{
		RecordingTunnels tunnels = new();
		SshTunnelStatus first = await OpenAsync(tunnels, "127.0.0.1", 8080, "web");

		SshTunnelStatus second = await OpenAsync(tunnels, "127.0.0.1", 8080, "web");

		Assert.Single(tunnels.Added);
		Assert.Equal(first.BoundPort, second.BoundPort);
	}

	// The editor's own default destination is localhost, which is where a port published everywhere goes too.
	[Fact]
	public async Task ASavedTunnelToTheSamePlace_IsUsed()
	{
		RecordingTunnels tunnels = new();
		tunnels.Seed(new SshTunnelStatus
		{
			Tunnel = SshTunnelDefinition.Create(SshTunnelKind.Local) with { ListenPort = 18080, DestinationHost = "localhost", DestinationPort = 8080 },
			State = SshTunnelState.Open,
			BoundPort = 18080,
		});

		SshTunnelStatus status = await OpenAsync(tunnels, "127.0.0.1", 8080, "web");

		Assert.Empty(tunnels.Added);
		Assert.Equal(18080, status.BoundPort);
	}

	[Theory]
	[InlineData(SshTunnelKind.Remote, "127.0.0.1")]
	[InlineData(SshTunnelKind.Local, "192.168.1.20")]
	public async Task ATunnelThisComputersBrowserCannotUse_IsLeftAlone(SshTunnelKind kind, string listenHost)
	{
		RecordingTunnels tunnels = new();
		tunnels.Seed(new SshTunnelStatus
		{
			Tunnel = SshTunnelDefinition.Create(kind) with { ListenHost = listenHost, ListenPort = 18080, DestinationHost = "127.0.0.1", DestinationPort = 8080 },
			State = SshTunnelState.Open,
			BoundPort = 18080,
		});

		await OpenAsync(tunnels, "127.0.0.1", 8080, "web");

		Assert.Single(tunnels.Added);
	}

	[Fact]
	public async Task OneOfOursThatFailed_OpensAgain_InsteadOfAddingAnother()
	{
		RecordingTunnels tunnels = new() { FailNext = "Another program already listens on 127.0.0.1:0." };
		SshTunnelStatus failed = await OpenAsync(tunnels, "10.0.0.5", 443, "api");
		Assert.Equal(SshTunnelState.Failed, failed.State);
		Assert.Equal("Another program already listens on 127.0.0.1:0.", failed.Error);

		SshTunnelStatus retried = await OpenAsync(tunnels, "10.0.0.5", 443, "api");

		Assert.Single(tunnels.Added);
		Assert.Equal([failed.Tunnel.Id], tunnels.Reopened);
		Assert.Equal(SshTunnelState.Open, retried.State);
	}

	[Fact]
	public async Task ALongName_IsCutToWhatATunnelKeeps()
	{
		RecordingTunnels tunnels = new();

		await OpenAsync(tunnels, "127.0.0.1", 8080, new string('n', 80));

		Assert.Equal(SshTunnelDefinition.MaxNameLength, Assert.Single(tunnels.Added).Name!.Length);
	}

	private static Task<SshTunnelStatus> OpenAsync(RecordingTunnels tunnels, string host, int port, string name) =>
		DockerPortTunnels.OpenAsync(tunnels, host, port, name, TestContext.Current.CancellationToken).WaitAsync(Patience, TestContext.Current.CancellationToken);

	/// <summary>Tunnels that open at once on a made-up port, or fail once when told to.</summary>
	private sealed class RecordingTunnels : ISshTunnelFeature
	{
		private readonly List<SshTunnelStatus> _tunnels = [];
		private int _nextPort = 50000;

		public event Action? Changed;

		public List<SshTunnelDefinition> Added { get; } = [];

		public List<Guid> Reopened { get; } = [];

		public string? FailNext { get; set; }

		public IReadOnlyList<SshTunnelStatus> Tunnels => [.. _tunnels];

		public int OpenCount => _tunnels.Count(status => status.State == SshTunnelState.Open);

		public void Seed(SshTunnelStatus status) => _tunnels.Add(status);

		public Task<SshTunnelStatus> AddAsync(SshTunnelDefinition tunnel, CancellationToken cancellationToken = default)
		{
			Added.Add(tunnel);
			_tunnels.Add(new SshTunnelStatus { Tunnel = tunnel, IsSessionOnly = true });
			return Task.FromResult(Open(tunnel.Id));
		}

		public Task<SshTunnelStatus> OpenAsync(Guid tunnelId, CancellationToken cancellationToken = default)
		{
			Reopened.Add(tunnelId);
			return Task.FromResult(Open(tunnelId));
		}

		public Task<SshTunnelStatus> CloseAsync(Guid tunnelId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task RemoveAsync(Guid tunnelId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		private SshTunnelStatus Open(Guid tunnelId)
		{
			int index = _tunnels.FindIndex(status => status.Tunnel.Id == tunnelId);
			SshTunnelStatus status = FailNext is { } error
				? _tunnels[index] with { State = SshTunnelState.Failed, Error = error, BoundPort = null }
				: _tunnels[index] with { State = SshTunnelState.Open, Error = null, BoundPort = _nextPort++ };
			FailNext = null;
			_tunnels[index] = status;
			Changed?.Invoke();
			return status;
		}
	}
}
