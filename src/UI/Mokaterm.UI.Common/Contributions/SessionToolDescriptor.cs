using Moka.Red.Core.Icons;
using Mokaterm.UI.Common.Components;

namespace Mokaterm.UI.Common.Contributions;

/// <summary>
/// A view a module adds to the session toolbar of one protocol, such as the SSH tunnels panel. The shell shows an
/// icon button per tool and opens the component in a dialog over the session.
/// </summary>
public sealed record SessionToolDescriptor
{
	/// <summary>
	/// The protocol the tool belongs to. Unlike protocol views, tools are not inherited: a variant such as <c>sftp</c>
	/// has no SSH client to forward through, so a tool that works there registers for it too.
	/// </summary>
	public required string ProtocolId { get; init; }

	/// <summary>Button tooltip and dialog title, for example "Port forwarding".</summary>
	public required string Title { get; init; }

	public required MokaIconDefinition Icon { get; init; }

	/// <summary>The view. Must derive from <see cref="SessionViewBase"/>.</summary>
	public required Type Component { get; init; }

	/// <summary>Sort key inside the toolbar; ties fall back to the title.</summary>
	public int Order { get; init; }

	/// <summary>False hides the button until the session is connected, which suits tools that talk to the server.</summary>
	public bool ShowWhileDisconnected { get; init; }
}
