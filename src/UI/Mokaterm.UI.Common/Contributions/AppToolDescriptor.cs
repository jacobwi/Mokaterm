using Moka.Red.Core.Icons;
using Mokaterm.UI.Common.ToolPages;

namespace Mokaterm.UI.Common.Contributions;

/// <summary>
/// A tool that needs no session, such as a subnet calculator. The shell lists it in the top bar's Tools menu, which only
/// shows while at least one is registered, and as a palette command, and opens it in a dialog or, on the desktop, in a
/// window of its own.
/// </summary>
public sealed record AppToolDescriptor
{
	/// <summary>
	/// The stable name its palette command (<c>mokaterm.app-tool.</c> and the id), a shortcut bound to it and its window's
	/// remembered place are saved under: lower case letters, digits and dashes. A later registration with the same id
	/// replaces an earlier one.
	/// </summary>
	public required string Id { get; init; }

	/// <summary>The menu entry and the title of its dialog or window, for example "Subnet calculator".</summary>
	public required string Title { get; init; }

	public required MokaIconDefinition Icon { get; init; }

	/// <summary>
	/// One line for the palette and the tool's header, such as "Network, broadcast and host range of an IPv4 or IPv6
	/// prefix".
	/// </summary>
	public string? Description { get; init; }

	/// <summary>The tool. Must derive from <see cref="AppToolBase"/>.</summary>
	public required Type Component { get; init; }

	/// <summary>Sort key in the Tools menu; ties fall back to the title.</summary>
	public int Order { get; init; }

	/// <summary>The size of the dialog the tool opens in, and the first size of its window.</summary>
	public SessionToolSize Size { get; init; }
}
