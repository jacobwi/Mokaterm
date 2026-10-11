using Microsoft.AspNetCore.Components.Web;
using Moka.Red.ContextMenu;
using Moka.Red.Core.Icons;

namespace Mokaterm.UI.Workspace;

/// <summary>
/// One thing the session toolbar can do. The same action is a button, an entry in the overflow menu and, for the
/// palette, something a request can press, so all three read it from here. Public only because
/// <see cref="SessionToolbar"/> takes a list of them and a public component cannot take a less visible type.
/// </summary>
public sealed record ToolbarAction
{
	/// <summary>The id from <c>SessionToolbarButtons</c>, which the hidden list and palette requests name it by.</summary>
	public required string Id { get; init; }

	/// <summary>The tooltip, and the menu entry unless <see cref="MenuText"/> says otherwise.</summary>
	public required string Title { get; init; }

	public required MokaIconDefinition Icon { get; init; }

	public required ToolbarGroup Group { get; init; }

	/// <summary>The keys bound to the same command, shown after the title.</summary>
	public string? Shortcut { get; init; }

	/// <summary>Lights the icon: a panel that is open, a tool that shows, typing that goes to other sessions.</summary>
	public bool Active { get; init; }

	/// <summary>Whether <see cref="Active"/> is an on or off state, which a menu shows as a check mark.</summary>
	public bool Toggle { get; init; }

	public ToolbarDot Dot { get; init; }

	public string? MenuText { get; init; }

	/// <summary>
	/// Shown whatever the hidden list says and never folded into the overflow menu, for a button that only appears when
	/// it is the thing to do next, such as Reconnect.
	/// </summary>
	public bool Pinned { get; init; }

	/// <summary>What a click does. The event places a menu the action opens at the pointer.</summary>
	public Func<MouseEventArgs, Task>? Run { get; init; }

	/// <summary>A menu of its own, such as the themes, which the overflow menu shows as a submenu instead of running it.</summary>
	public Func<IReadOnlyList<MokaContextMenuItem>>? Children { get; init; }
}
