using Microsoft.AspNetCore.Components;
using Mokaterm.Modules.Ssh.Tunnels;

namespace Mokaterm.Modules.Ssh.Components;

/// <summary>The tunnels saved with a connection, edited in place inside the connection editor.</summary>
public partial class SshTunnelList : ComponentBase
{
	private Guid? _editing;

	[Parameter, EditorRequired]
	public IReadOnlyList<SshTunnelDefinition> Tunnels { get; set; } = [];

	[Parameter]
	public EventCallback<IReadOnlyList<SshTunnelDefinition>> TunnelsChanged { get; set; }

	[Parameter]
	public bool Disabled { get; set; }

	private bool IsFull => Tunnels.Count >= SshConnectionOptions.MaxTunnels;

	internal static string KindLabel(SshTunnelKind kind) => kind switch
	{
		SshTunnelKind.Remote => "REMOTE",
		SshTunnelKind.Dynamic => "SOCKS",
		_ => "LOCAL",
	};

	private Task AddAsync(SshTunnelKind kind)
	{
		if (IsFull)
		{
			return Task.CompletedTask;
		}

		SshTunnelDefinition tunnel = SshTunnelDefinition.Create(kind);
		_editing = tunnel.Id;
		return TunnelsChanged.InvokeAsync([.. Tunnels, tunnel]);
	}

	private Task ReplaceAsync(SshTunnelDefinition tunnel) =>
		TunnelsChanged.InvokeAsync([.. Tunnels.Select(existing => existing.Id == tunnel.Id ? tunnel : existing)]);

	private Task RemoveAsync(Guid tunnelId)
	{
		if (_editing == tunnelId)
		{
			_editing = null;
		}

		return TunnelsChanged.InvokeAsync([.. Tunnels.Where(existing => existing.Id != tunnelId)]);
	}

	private void Toggle(Guid tunnelId) => _editing = _editing == tunnelId ? null : tunnelId;
}
