using Mokaterm.UI.Common.Contributions;

namespace Mokaterm.UI.Common.Platform;

/// <summary>A session tool to show in a window of its own.</summary>
/// <param name="SessionId">The session the tool works on.</param>
/// <param name="ToolKey">The tool's <c>SessionToolDescriptor.Key</c>.</param>
/// <param name="Title">The window's title, such as "Docker · user@host".</param>
/// <param name="Services">
/// The services of the window that asks: its vault, its sessions and its settings. The new window borrows them rather
/// than starting a scope of its own, which would have a locked vault and no sessions.
/// </param>
/// <param name="Size">How much room the tool asks for, which sets the window's first size.</param>
/// <param name="Bounds">Where this tool's window was last time, or null for the host's default.</param>
/// <param name="BoundsChanged">Told where the window ends up, so the next one opens there.</param>
public sealed record ToolWindowRequest(
	Guid SessionId,
	string ToolKey,
	string Title,
	IServiceProvider Services,
	SessionToolSize Size,
	ToolWindowBounds? Bounds,
	Action<ToolWindowBounds>? BoundsChanged);
