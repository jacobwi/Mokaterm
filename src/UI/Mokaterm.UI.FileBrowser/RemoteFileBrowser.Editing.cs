using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Formatting;
using Mokaterm.UI.Common.Icons;
using Mokaterm.UI.FileBrowser.Browsing;
using Mokaterm.UI.FileBrowser.Editing;

namespace Mokaterm.UI.FileBrowser;

/// <summary>Opening a remote text file in the built-in editor and writing it back in place.</summary>
public sealed partial class RemoteFileBrowser
{
	// What a new file is called until the user types something else; the base name is selected, so typing replaces it.
	private const string NewFileName = "untitled.txt";

	/// <summary>
	/// Reads the file, shows the editor, and saves through the file system's own replace, which keeps the owner and
	/// mode the file already had. Anything the editor could not hand back unchanged is refused up front rather than
	/// opened and quietly rewritten on save.
	/// </summary>
	private async Task EditAsync(RemoteFileEntry entry)
	{
		if (_fileSystem is not { } fileSystem || entry.IsDirectoryLike)
		{
			return;
		}

		CancellationToken token = ConnectionToken;
		int maxBytes = Settings.Get<FileTransferSettings>().Clamped().MaxEditKilobytes * 1024;

		RemoteTextFile? file;
		TextFileRefusal refusal;
		RemoteFileEntry? opened;
		try
		{
			await using Stream source = await fileSystem.OpenReadAsync(entry.Path, token);
			(file, refusal) = await RemoteTextFile.ReadAsync(source, maxBytes, token);

			// What the file looked like when it was read, so a save can notice someone else changing it meanwhile.
			opened = file is null ? null : await fileSystem.StatAsync(entry.Path, token);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			ShowAlert("Could not open " + entry.Name, ex, retryAsRoot: _ => EditAsync(entry));
			return;
		}
		catch (OperationCanceledException)
		{
			return;
		}

		if (file is null)
		{
			Interaction.Notify(NoticeSeverity.Warning, RefusalText(refusal, maxBytes), "Cannot edit " + entry.Name);
			return;
		}

		RemoteFileEdit edit = new(fileSystem, entry.Path, file, opened, maxBytes);
		bool saved = await Dialogs.EditFileAsync(
			entry.Name,
			entry.Path,
			file.Text,
			RemoteFileEdit.Facts(file, opened?.Size ?? entry.Size),
			(text, overwrite) => edit.SaveAsync(text, overwrite, ConnectionToken),
			edit.AcceptServerCopy,
			ConfirmDiscardEditAsync);

		if (saved)
		{
			await RefreshAsync(entry.Path);
		}
	}

	/// <summary>
	/// Asks for a name, creates an empty file of that name in the current folder and opens it in the editor. A taken name
	/// is refused in the dialog, and the server refuses it again when the listing on screen was out of date. It runs as
	/// the browser's account, with the usual offer to retry as root, after which the editor opens as root.
	/// </summary>
	private async Task CreateFileAsync()
	{
		if (!BeginOperation())
		{
			return;
		}

		try
		{
			string directory = _path;
			HashSet<string> taken = TakenNames(except: null);
			string? name = await Dialogs.AskNameAsync("New file", MokatermIcons.FilePlus, "File name", EntryNames.Unique(NewFileName, taken, isFolder: false), "Create", taken, selectBaseName: true);
			if (name is null)
			{
				return;
			}

			string path = RemotePath.Combine(directory, name);
			await RunChangeAsync(
				"Could not create " + name,
				(fileSystem, token) => fileSystem.UploadAsync(path, Stream.Null, UploadOptions.Default, progress: null, token).AsTask(),
				select: path,
				then: () => EditCreatedAsync(path));
		}
		finally
		{
			EndOperation();
		}
	}

	// The listing, not the view: a new ".env" is in the folder even while hidden files are not shown.
	private Task EditCreatedAsync(string path) =>
		_path == RemotePath.GetParent(path) && _entries.FirstOrDefault(entry => entry.Path == path) is { } created
			? EditAsync(created)
			: Task.CompletedTask;

	private async Task<bool> ConfirmDiscardEditAsync()
	{
		ConfirmPrompt prompt = new()
		{
			Title = "Discard changes",
			Message = "The edits have not been saved. Closing the editor throws them away.",
			ConfirmText = "Discard",
			Destructive = true,
		};
		return (await Interaction.ConfirmAsync(prompt, ConnectionToken)).Confirmed;
	}

	private static string RefusalText(TextFileRefusal refusal, int maxBytes) => refusal switch
	{
		TextFileRefusal.TooLarge =>
			$"It is bigger than {DisplayFormat.Bytes(maxBytes)}, the largest the editor opens. Download it, or raise the limit in Settings, Files and transfers.",
		TextFileRefusal.Binary =>
			"It holds bytes that are not text, so an editor would corrupt it. Download it instead.",
		_ =>
			"It is not UTF-8 text. Editing it here would write the editor's own characters back over the server's bytes, so download it instead.",
	};
}
