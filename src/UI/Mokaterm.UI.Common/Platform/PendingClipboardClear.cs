namespace Mokaterm.UI.Common.Platform;

/// <summary>A copied secret whose clear <see cref="ClipboardClearSchedule"/> still owes.</summary>
/// <param name="Copy">The copy's number in the schedule.</param>
/// <param name="ContentNumber">
/// The clipboard's number right after the copy (<see cref="ISystemClipboard.ContentNumber"/>), or null when there is no
/// system clipboard or it could not be read, which leaves the clear to a page.
/// </param>
/// <param name="WaitsForFocus">True when a page took the clear and holds it until that page is focused again.</param>
internal readonly record struct PendingClipboardClear(long Copy, uint? ContentNumber, bool WaitsForFocus);
