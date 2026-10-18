namespace Mokaterm.UI.Common.Files;

/// <summary>
/// The built-in file browser, the one a session tab docks beside its terminal, shown in a dialog over a file system a
/// module brings: a container's files, a volume, anything that is not the session's own. Everything the browser does
/// works on it as on a session's files, uploads, downloads, the editor, search and the transfer queue with its retries,
/// and what the file system cannot do (root, checksums, archives) is left out of the menus rather than offered and
/// refused.
/// </summary>
/// <remarks>
/// Scoped to the page, like the dialogs it opens: a session tool in its own window gets the browser in that window. Call
/// it from the renderer's thread.
/// </remarks>
public interface IFileBrowserDialog
{
	/// <summary>
	/// Shows the browser and returns once the user closes it, or when <paramref name="cancellationToken"/> is cancelled,
	/// which closes it. Transfers it queued carry on in the transfer queue either way.
	/// </summary>
	Task ShowAsync(FileBrowserRequest request, CancellationToken cancellationToken = default);

	/// <summary>
	/// Shows the browser with a "Use this folder" button under it and returns the folder the browser showed when that
	/// was pressed, as an absolute path; null when it was closed without one, or when <paramref name="cancellationToken"/>
	/// was cancelled. Everything else in the browser works as in <see cref="ShowAsync"/>.
	/// </summary>
	Task<string?> PickFolderAsync(FileBrowserRequest request, CancellationToken cancellationToken = default);
}
