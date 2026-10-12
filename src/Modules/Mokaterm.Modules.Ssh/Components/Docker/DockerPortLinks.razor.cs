using Microsoft.AspNetCore.Components;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.Modules.Ssh.Tunnels;
using Mokaterm.UI.Common.Platform;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// Opens a container's published ports in the browser. On the desktop each one goes through a session-only tunnel from
/// a free port on this computer, reused while it stays open; on the web host, where a tunnel would end on the server,
/// only a port published on every interface opens, straight at the session's host, and the others say why not.
/// </summary>
public sealed partial class DockerPortLinks : DockerManagerPageBase
{
	private IReadOnlyList<DockerPortRoute> _routes = [];
	private bool _opening;

	/// <summary>The container's ports; only published TCP ones are offered.</summary>
	[Parameter, EditorRequired]
	public IReadOnlyList<DockerPort> Ports { get; set; } = [];

	/// <summary>The container or compose service, for the tunnel's name and the button's label.</summary>
	[Parameter, EditorRequired]
	public string Name { get; set; } = "";

	[Inject]
	private IUrlOpener Urls { get; set; } = default!;

	[Inject]
	private IAppEnvironment App { get; set; } = default!;

	protected override void OnParametersSet()
	{
		bool desktop = App.Kind == HostKind.Desktop;
		bool throughJumpHosts = SshConnectionOptions.FromOptions(Session.Connection.Options).JumpConnectionIds.Count > 0;
		_routes = [.. DockerPortAccess.Openable(Ports).Select(port => DockerPortAccess.Route(port, desktop, Session.Host.Address, throughJumpHosts))];
	}

	private static string Hint(DockerPortRoute route, string scheme) => route.Kind == DockerPortRouteKind.Direct
		? $"Open {route.Link(scheme)}"
		: $"Open {route.Port.Display} over {scheme} through a tunnel from this computer";

	private async Task OpenAsync(DockerPortRoute route, string scheme)
	{
		if (_opening || !route.CanOpen)
		{
			return;
		}

		if (route.Kind == DockerPortRouteKind.Direct)
		{
			await OpenLinkAsync(route.Link(scheme));
			return;
		}

		if (Session.Session?.GetFeature<ISshTunnelFeature>() is not { } tunnels)
		{
			Interaction.Notify(NoticeSeverity.Warning, "This session has no port forwarding to open the page through.");
			return;
		}

		_opening = true;
		try
		{
			string name = $"{Name} port {route.Port.ContainerPort}";
			SshTunnelStatus status = await DockerPortTunnels.OpenAsync(tunnels, route.Host, route.HostPort, name, Lifetime);
			if (status is { State: SshTunnelState.Open, BoundPort: { } bound })
			{
				await OpenLinkAsync(route.Link(scheme, bound));
			}
			else
			{
				Interaction.Notify(NoticeSeverity.Error, status.Error ?? "The tunnel did not open.", $"Could not open {route.Port.Display}");
			}
		}
		catch (OperationCanceledException)
		{
			// The panel closed while the tunnel was opening; the tunnel stays listed under Port forwarding.
		}
		catch (ObjectDisposedException)
		{
			Interaction.Notify(NoticeSeverity.Warning, "The session closed before the tunnel opened.");
		}
		catch (Exception ex) when (Describe(ex) is { } problem)
		{
			Interaction.Notify(NoticeSeverity.Error, problem, $"Could not open {route.Port.Display}");
		}
		finally
		{
			_opening = false;
		}
	}

	private async Task OpenLinkAsync(Uri? link)
	{
		if (link is null || !await Urls.OpenAsync(link.AbsoluteUri, Lifetime))
		{
			Interaction.Notify(NoticeSeverity.Warning, link is null ? "There is no address to open for this port." : $"The browser did not open {link.AbsoluteUri}.");
		}
	}
}
