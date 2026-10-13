using Mokaterm.UI.Common.Contributions;

namespace Mokaterm.UI.Common.Platform;

/// <summary>A tool to show in a window of its own.</summary>
/// <param name="Target">The session tool or app tool the window shows.</param>
/// <param name="Title">The window's title, such as "Docker · user@host" or "Subnet calculator · Mokaterm".</param>
/// <param name="Services">
/// The services of the window that asks: its vault, its sessions and its settings. The new window borrows them rather
/// than starting a scope of its own, which would have a locked vault and no sessions.
/// </param>
/// <param name="Size">How much room the tool asks for, which sets the window's first size.</param>
/// <param name="Bounds">Where this tool's window was last time and whether it stayed on top, or null for the host's default.</param>
/// <param name="BoundsChanged">
/// Told where the window ends up and whether it stays on top, so the next one opens the same way.
/// </param>
public sealed record ToolWindowRequest(
	ToolWindowTarget Target,
	string Title,
	IServiceProvider Services,
	SessionToolSize Size,
	ToolWindowBounds? Bounds,
	Action<ToolWindowBounds>? BoundsChanged)
{
	/// <summary>
	/// Told when the user closed the window with its own close button. Not told when the app closed it: its session
	/// went away, the vault locked, or Mokaterm itself is closing.
	/// </summary>
	public Action? ClosedByUser { get; init; }
}
