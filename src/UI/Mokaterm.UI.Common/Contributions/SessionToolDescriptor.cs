using Moka.Red.Core.Icons;
using Mokaterm.Abstractions.Contributions;
using Mokaterm.UI.Common.Components;

namespace Mokaterm.UI.Common.Contributions;

/// <summary>
/// A view a module adds to the session toolbar of one protocol, such as the SSH tunnels panel. The shell shows it in
/// the toolbar, in the command palette, and opens the component in a dialog over the session or, on the desktop, in a
/// window of its own.
/// </summary>
public sealed record SessionToolDescriptor : IContributionIdentity
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

	/// <summary>
	/// A stable name such as <c>docker</c>, which the palette command, a shortcut bound to it, the toolbar's hidden
	/// buttons and a remembered window size are saved under. Tools on different protocols that share it are one
	/// command. Empty means the title, which changes whenever the title does.
	/// </summary>
	public string? Id { get; init; }

	/// <summary>Sort key inside the toolbar; ties fall back to the title.</summary>
	public int Order { get; init; }

	/// <summary>False hides the button until the session is connected, which suits tools that talk to the server.</summary>
	public bool ShowWhileDisconnected { get; init; }

	/// <summary>The size of the dialog the tool opens in, and the first size of its window.</summary>
	public SessionToolSize Size { get; init; }

	/// <summary><see cref="Id"/>, or the title when there is none, lower case with dashes for spaces.</summary>
	public string Key => string.Join(
		'-',
		(Id is { Length: > 0 } id ? id : Title).ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

	string IContributionIdentity.Kind => "session tool";
	string IContributionIdentity.Id => Key;
	string? IContributionIdentity.Scope => ProtocolId;
}
