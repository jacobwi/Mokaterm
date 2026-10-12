using System.Globalization;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Ssh.Tunnels;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>How one published port is opened in the browser from here, or why it cannot be.</summary>
internal sealed record DockerPortRoute
{
	public required DockerPort Port { get; init; }

	public required DockerPortRouteKind Kind { get; init; }

	/// <summary>
	/// Where the connection goes: a tunnel's destination as the docker host sees it, or the address the browser opens
	/// for a direct link. Empty when refused.
	/// </summary>
	public string Host { get; init; } = "";

	/// <summary>The port on <see cref="Host"/>, the one the container is published on.</summary>
	public int HostPort { get; init; }

	/// <summary><c>http</c> or <c>https</c>, both when the port does not say which, none when refused.</summary>
	public IReadOnlyList<string> Schemes { get; init; } = [];

	/// <summary>Why the port cannot be opened from here, for <see cref="DockerPortRouteKind.Refused"/>.</summary>
	public string? Reason { get; init; }

	public bool CanOpen => Kind != DockerPortRouteKind.Refused;

	/// <summary>
	/// The address to open: the session's host for a direct link, or this machine's end of the tunnel, which listens on
	/// <paramref name="tunnelPort"/>. Null when refused, or when the pieces do not make a web link.
	/// </summary>
	public Uri? Link(string scheme, int? tunnelPort = null)
	{
		ArgumentNullException.ThrowIfNull(scheme);
		string? endpoint = Kind switch
		{
			DockerPortRouteKind.Direct => HostEndpoint.Format(Host, HostPort),
			DockerPortRouteKind.Tunnel when tunnelPort is { } port => HostEndpoint.Format(SshTunnelDefinition.DefaultListenHost, port),
			_ => null,
		};

		return endpoint is not null && WebLink.TryCreate(string.Create(CultureInfo.InvariantCulture, $"{scheme}://{endpoint}/"), out Uri? link)
			? link
			: null;
	}
}
