using System.Globalization;
using Microsoft.AspNetCore.Components;
using Mokaterm.Modules.Ssh.Tunnels;
using Mokaterm.UI.Common.Components;

namespace Mokaterm.Modules.Ssh.Components;

/// <summary>
/// The fields of one forwarded port, shared by the connection editor and the tunnels panel of a live session. It only
/// reports a new definition; whoever owns the tunnel decides what to do with it.
/// </summary>
public partial class SshTunnelForm : ComponentBase
{
	private readonly FieldDraft<string> _listenHost = new();
	private readonly FieldDraft<string> _listenPort = new();
	private readonly FieldDraft<string> _destinationHost = new();
	private readonly FieldDraft<string> _destinationPort = new();

	[Parameter, EditorRequired]
	public SshTunnelDefinition Tunnel { get; set; } = SshTunnelDefinition.Create(SshTunnelKind.Local);

	[Parameter]
	public EventCallback<SshTunnelDefinition> TunnelChanged { get; set; }

	/// <summary>False in a live session, where a tunnel is opened right away instead of being marked to open later.</summary>
	[Parameter]
	public bool ShowOpenWithSession { get; set; } = true;

	[Parameter]
	public bool Disabled { get; set; }

	private string KindHelp => Tunnel.Kind switch
	{
		SshTunnelKind.Remote => "The server listens and sends what arrives to a host this machine can reach.",
		SshTunnelKind.Dynamic => "A SOCKS5 proxy here that the server connects for.",
		_ => "This machine listens and sends what arrives to a host the server can reach.",
	};

	private string ListenHostLabel => Tunnel.Kind == SshTunnelKind.Remote ? "Listen address on the server" : "Listen address";

	private string ListenHostHelp => Tunnel.Kind == SshTunnelKind.Remote
		? "The server binds this. Anything but localhost needs GatewayPorts on the server."
		: "127.0.0.1 keeps the tunnel on this machine. 0.0.0.0 opens it to the network.";

	private string ListenPortHelp => Tunnel.Kind == SshTunnelKind.Remote
		? "The port the server listens on."
		: "0 picks a free port.";

	private string DestinationHelp => Tunnel.Kind == SshTunnelKind.Remote
		? "Resolved from this machine."
		: "Resolved from the server, so localhost means the server itself.";

	private static string PortText(int port) => port.ToString(CultureInfo.InvariantCulture);

	private Task SetKindAsync(string value)
	{
		if (!Enum.TryParse(value, out SshTunnelKind kind) || !Enum.IsDefined(kind) || kind == Tunnel.Kind)
		{
			return Task.CompletedTask;
		}

		_listenPort.Clear();
		_destinationHost.Clear();
		_destinationPort.Clear();
		SshTunnelDefinition changed = Tunnel with { Kind = kind };

		// A tunnel that came from SOCKS has no destination, and a remote one cannot listen on port 0: fill both in so
		// the switch never leaves a tunnel the editor would refuse to save.
		if (kind != SshTunnelKind.Dynamic)
		{
			changed = changed with
			{
				DestinationHost = changed.DestinationHost.Length == 0 ? "localhost" : changed.DestinationHost,
				DestinationPort = changed.DestinationPort == 0 ? SshTunnelDefinition.DefaultForwardPort : changed.DestinationPort,
			};
		}

		if (kind == SshTunnelKind.Remote && changed.ListenPort == 0)
		{
			changed = changed with { ListenPort = SshTunnelDefinition.DefaultForwardPort };
		}

		return ChangeAsync(changed);
	}

	private Task SetNameAsync(string value) =>
		ChangeAsync(Tunnel with { Name = string.IsNullOrWhiteSpace(value) ? null : value });

	private Task SetListenHostAsync(string value)
	{
		if (!SshTunnelDefinition.IsValidHost(value))
		{
			_listenHost.Reject(value, "Enter a host name or address with no spaces.");
			return Task.CompletedTask;
		}

		_listenHost.Clear();
		return ChangeAsync(Tunnel with { ListenHost = value });
	}

	private Task SetDestinationHostAsync(string value)
	{
		if (!SshTunnelDefinition.IsValidHost(value))
		{
			_destinationHost.Reject(value, "Enter a host name or address with no spaces.");
			return Task.CompletedTask;
		}

		_destinationHost.Clear();
		return ChangeAsync(Tunnel with { DestinationHost = value });
	}

	private Task SetListenPortAsync(string value)
	{
		bool allowZero = Tunnel.Kind != SshTunnelKind.Remote;
		if (!TryParsePort(value, allowZero, out int port))
		{
			_listenPort.Reject(value, allowZero ? "Enter a port from 0 to 65535." : "Enter a port from 1 to 65535.");
			return Task.CompletedTask;
		}

		_listenPort.Clear();
		return ChangeAsync(Tunnel with { ListenPort = port });
	}

	private Task SetDestinationPortAsync(string value)
	{
		if (!TryParsePort(value, allowZero: false, out int port))
		{
			_destinationPort.Reject(value, "Enter a port from 1 to 65535.");
			return Task.CompletedTask;
		}

		_destinationPort.Clear();
		return ChangeAsync(Tunnel with { DestinationPort = port });
	}

	private Task SetOpenWithSessionAsync(bool value) => ChangeAsync(Tunnel with { OpenWithSession = value });

	private static bool TryParsePort(string value, bool allowZero, out int port) =>
		int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out port)
		&& SshTunnelDefinition.IsValidPort(port, allowZero);

	private Task ChangeAsync(SshTunnelDefinition tunnel) => TunnelChanged.InvokeAsync(tunnel);
}
