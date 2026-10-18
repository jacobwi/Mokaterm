namespace Mokaterm.UI.Common.Platform;

/// <summary>
/// The clear a window still owes a copied secret, for a host whose app is going away before the deadline: on the
/// desktop the clipboard outlives the process, and the page that would have cleared it goes with the process. One per
/// window and its tool windows, over the same state the timed clear uses.
/// </summary>
public interface IPendingClipboardClear
{
	/// <summary>
	/// Empties the clipboard through <see cref="ISystemClipboard"/> while a copied secret's clear is still due, either
	/// because its deadline has not come or because a page put it off until it is focused again, and only when nothing
	/// has written the clipboard since the copy. Null when no clear is due or there is no system clipboard to clear
	/// through. Blocks the caller for about <paramref name="timeout"/> at most; never throws.
	/// </summary>
	SystemClipboardClear? ClearNow(TimeSpan timeout);
}
