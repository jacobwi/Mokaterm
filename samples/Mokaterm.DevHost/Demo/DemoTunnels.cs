using Mokaterm.Modules.Ssh.Tunnels;

namespace Mokaterm.DevHost.Demo;

/// <summary>
/// Forwarded ports that forward nothing, so the real tunnels panel can be looked at without an SSH server. It seeds one
/// tunnel of each state, and anything added here opens after a short delay.
/// </summary>
internal sealed class DemoTunnels : ISshTunnelFeature
{
	private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(400);

	private readonly Lock _lock = new();
	private readonly List<SshTunnelStatus> _tunnels;
	private readonly TimeProvider _timeProvider;

	public DemoTunnels(TimeProvider timeProvider)
	{
		_timeProvider = timeProvider;
		_tunnels =
		[
			Seed(SshTunnelKind.Local, "Database", 15432, "db.internal", 5432, SshTunnelState.Open, boundPort: 15432),
			Seed(SshTunnelKind.Dynamic, "Proxy", 1080, "", 0, SshTunnelState.Closed),
			Seed(SshTunnelKind.Remote, "Webhook", 9000, "127.0.0.1", 3000, SshTunnelState.Failed) with
			{
				Error = "The server refused to listen on 127.0.0.1:9000. It may need GatewayPorts, or the port may be taken.",
			},
		];
	}

	public event Action? Changed;

	public IReadOnlyList<SshTunnelStatus> Tunnels
	{
		get
		{
			lock (_lock)
			{
				return [.. _tunnels];
			}
		}
	}

	public int OpenCount
	{
		get
		{
			lock (_lock)
			{
				return _tunnels.Count(status => status.State == SshTunnelState.Open);
			}
		}
	}

	public async Task<SshTunnelStatus> AddAsync(SshTunnelDefinition tunnel, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(tunnel);
		lock (_lock)
		{
			_tunnels.Add(new SshTunnelStatus { Tunnel = tunnel, State = SshTunnelState.Opening, IsSessionOnly = true });
		}

		Changed?.Invoke();
		return await OpenAsync(tunnel.Id, cancellationToken);
	}

	public async Task<SshTunnelStatus> OpenAsync(Guid tunnelId, CancellationToken cancellationToken = default)
	{
		await Task.Delay(Delay, _timeProvider, cancellationToken);
		return Update(tunnelId, status => status with
		{
			State = SshTunnelState.Open,
			Error = null,
			BoundPort = status.Tunnel.ListenPort == 0 ? Random.Shared.Next(49152, 65535) : status.Tunnel.ListenPort,
		});
	}

	public async Task<SshTunnelStatus> CloseAsync(Guid tunnelId, CancellationToken cancellationToken = default)
	{
		await Task.Delay(Delay, _timeProvider, cancellationToken);
		return Update(tunnelId, status => status with { State = SshTunnelState.Closed, Error = null, BoundPort = null });
	}

	public Task RemoveAsync(Guid tunnelId, CancellationToken cancellationToken = default)
	{
		lock (_lock)
		{
			_tunnels.RemoveAll(status => status.Tunnel.Id == tunnelId);
		}

		Changed?.Invoke();
		return Task.CompletedTask;
	}

	private static SshTunnelStatus Seed(
		SshTunnelKind kind,
		string name,
		int listenPort,
		string destinationHost,
		int destinationPort,
		SshTunnelState state,
		int? boundPort = null) =>
		new()
		{
			Tunnel = SshTunnelDefinition.Create(kind) with
			{
				Name = name,
				ListenPort = listenPort,
				DestinationHost = destinationHost,
				DestinationPort = destinationPort,
			},
			State = state,
			BoundPort = boundPort,
		};

	private SshTunnelStatus Update(Guid tunnelId, Func<SshTunnelStatus, SshTunnelStatus> change)
	{
		SshTunnelStatus updated;
		lock (_lock)
		{
			int index = _tunnels.FindIndex(status => status.Tunnel.Id == tunnelId);
			if (index < 0)
			{
				throw new KeyNotFoundException($"No tunnel with id {tunnelId}.");
			}

			updated = change(_tunnels[index]);
			_tunnels[index] = updated;
		}

		Changed?.Invoke();
		return updated;
	}
}
