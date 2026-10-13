namespace Mokaterm.UI.Shell;

/// <summary>
/// A tool window to bring back at the next start, in the plain <c>ui-state</c> document, so ids only: an app tool's id,
/// or a session tool's key and the id of the saved login its session was opened from.
/// </summary>
/// <param name="Tool">The app tool's id or the session tool's key.</param>
/// <param name="ConnectionId">The login of the tool's session; null for an app tool.</param>
internal sealed record ToolWindowMemo(string Tool, Guid? ConnectionId);
