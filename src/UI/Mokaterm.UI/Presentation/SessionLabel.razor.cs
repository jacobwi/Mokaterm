using Microsoft.AspNetCore.Components;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Sessions;

namespace Mokaterm.UI.Presentation;

/// <summary>
/// A session written as its parts rather than one string: the login's name, the user, and the machine in the colour
/// that machine is drawn in everywhere else.
/// </summary>
public sealed partial class SessionLabel : ComponentBase
{
	/// <summary>The session to describe. Takes the place of <see cref="Host"/> and <see cref="Connection"/>.</summary>
	[Parameter]
	public ISessionHandle? Session { get; set; }

	[Parameter]
	public HostProfile? Host { get; set; }

	[Parameter]
	public ConnectionProfile? Connection { get; set; }

	/// <summary>Shows the login's label, or the machine's name when it has one. On by default.</summary>
	[Parameter]
	public bool ShowName { get; set; } = true;

	/// <summary>Shows the port after the address. Off by default: the default port says nothing.</summary>
	[Parameter]
	public bool ShowPort { get; set; }

	[Parameter]
	public bool Mono { get; set; }

	/// <summary>The title attribute, such as the full endpoint. Null leaves the label without one.</summary>
	[Parameter]
	public string? Tooltip { get; set; }

	private HostProfile? CurrentHost => Session?.Host ?? Host;

	private ConnectionProfile? CurrentConnection => Session?.Connection ?? Connection;

	private string Address => CurrentHost?.Address ?? "";

	private string Accent => CurrentHost is { } host ? HostAccent.For(host) : HostAccent.For(Address);

	private string? Username => CurrentConnection?.Username;

	private int? Port
	{
		get
		{
			// A serial line has no port, so there is no number to show even when one was asked for.
			if (!ShowPort || Session?.Protocol.UsesPort == false)
			{
				return null;
			}

			return CurrentConnection?.Port ?? Session?.Protocol.DefaultPort;
		}
	}

	private string? Name =>
		ShowName && CurrentHost is { } host && CurrentConnection is { } connection ? SessionTitle.Name(host, connection) : null;
}
