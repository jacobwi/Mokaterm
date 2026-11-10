namespace Mokaterm.UI.Common.Platform;

/// <summary>
/// What the window is called where the system shows it: the taskbar and Alt+Tab on the desktop, the browser tab on the
/// web. The shell names the active session; the host puts the app's own name in front of it. The page's own default
/// writes the document title, and the desktop host replaces it with the native window's title.
/// </summary>
/// <remarks>
/// The title is seen outside the app, in window lists and screen shares, so it carries only what the session's tab
/// already shows: a name, a user and a machine, never a secret.
/// </remarks>
public interface IWindowTitle
{
	/// <summary>Shows <paramref name="subject"/> after the app's name, or the app's name alone for null.</summary>
	Task ShowAsync(string? subject);
}
