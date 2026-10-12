namespace Mokaterm.UI.Common.Platform;

/// <summary>
/// Opens a web address in the user's browser: a new tab on the web host, and the system browser on the desktop, whose
/// WebView hands every new window outside the app to the system. Only an absolute http or https address gets through
/// (<see cref="Abstractions.Platform.WebLink"/>), checked here and again in the page, because a page's own new window
/// skips the desktop host's link policy.
/// </summary>
/// <remarks>
/// Scoped to the page: from a session tool in its own window it opens from that window. Call it straight from the
/// user's click, since a browser lets a page open a tab only shortly after the user did something.
/// </remarks>
public interface IUrlOpener
{
	/// <returns>False when nothing was opened: the address is not an http or https link, or the page is gone.</returns>
	ValueTask<bool> OpenAsync(string address, CancellationToken cancellationToken = default);
}
