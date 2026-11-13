namespace Mokaterm.UI.Common.Platform;

/// <summary>
/// Marks a button or menu item whose click copies through <see cref="IClipboardService"/>. Safari, iOS and Firefox let a
/// page write the clipboard only while the click is still live, and on Blazor Server a click handler runs a round trip
/// later, so the page starts the write inside the click and the text the handler writes next fills it. Without the mark
/// the copy still works where the browser allows a late write (Chromium, the desktop app) and fails elsewhere.
/// </summary>
/// <remarks>
/// Put <see cref="Class"/> on the element the user clicks: <c>ButtonClass</c> on an <c>IconButton</c>, <c>Class</c> on a
/// <c>MokaButton</c>, <c>CssClass</c> on a <c>MokaContextMenuItem</c>. The handler has a few seconds to write; a click that
/// ends up copying nothing leaves the clipboard as it was. Nothing of the text goes into the page's markup, so it is the
/// mark for secrets as well.
/// </remarks>
public static class CopyClick
{
	public const string Class = "mt-copy-click";
}
