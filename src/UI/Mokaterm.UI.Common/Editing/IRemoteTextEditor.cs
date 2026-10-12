namespace Mokaterm.UI.Common.Editing;

/// <summary>
/// The built-in text editor for a file on any remote file system: the one the file browser opens with F4, offered to
/// modules too, which cannot reach the browser itself. It reads the file under the editor's size cap, refuses what it
/// could not write back unchanged, shows the editor, and saves through the file system's own replace, which keeps the
/// file's owner and mode, asking before writing over a change made on the server meanwhile.
/// </summary>
/// <remarks>
/// Scoped to the page, like the dialogs it opens: a session tool in its own window gets the editor of that window. Call
/// it from the renderer's thread. Service dialogs stack, so it may be called from inside another one, as the file
/// browser does when it is itself in a dialog.
/// </remarks>
public interface IRemoteTextEditor
{
	/// <summary>
	/// Edits <see cref="RemoteTextEditRequest.Path"/> until the user saves or closes the editor. A file that is not text,
	/// or is over the cap, is refused with a notice that says why. A file that cannot be read at all is not reported here:
	/// the result carries the failure, because only the caller knows what to offer next (running it as root, another
	/// file).
	/// </summary>
	Task<RemoteTextEditResult> EditAsync(RemoteTextEditRequest request, CancellationToken cancellationToken = default);
}
