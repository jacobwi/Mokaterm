using Moka.Red.Data.FileBrowser;

namespace Mokaterm.UI.FileBrowser.Browsing;

/// <summary>
/// How the last file browser in this window was left: list or tiles, the sort, and whether the details pane was open.
/// One per window or circuit, so a browser mounted again (a pane move, a vault lock, another session) starts the way
/// the last one was left instead of from the defaults. It is never written to settings.
/// </summary>
internal sealed class FileBrowserViewMemory
{
	public MokaFileView View { get; set; } = new();

	public bool DetailsOpen { get; set; }
}
