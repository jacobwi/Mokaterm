using Microsoft.AspNetCore.Components;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.UI.Common.Editing;
using Mokaterm.UI.Common.Icons;
using Mokaterm.UI.FileBrowser.Browsing;

namespace Mokaterm.UI.FileBrowser;

/// <summary>Opening a remote text file in the built-in editor and writing it back in place.</summary>
public sealed partial class RemoteFileBrowser
{
	// What a new file is called until the user types something else; the base name is selected, so typing replaces it.
	private const string NewFileName = "untitled.txt";

	[Inject]
	private IRemoteTextEditor Editor { get; set; } = default!;

	/// <summary>Edits the file with the shared editor, which modules use too; only the failure alert is the browser's.</summary>
	private async Task EditAsync(RemoteFileEntry entry)
	{
		if (_fileSystem is not { } fileSystem || entry.IsDirectoryLike)
		{
			return;
		}

		await EditFileAsync(fileSystem, entry.Path, entry.Name, createNew: false, "Could not open " + entry.Name);
	}

	/// <summary>
	/// Asks for a name and has the editor create an empty file of that name in the current folder and open it. A taken
	/// name is refused in the dialog, and the server refuses it again when the listing on screen was out of date. It runs
	/// as the browser's account, with the usual offer to retry as root, after which the editor opens as root.
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
			if (name is null || _fileSystem is not { } fileSystem)
			{
				return;
			}

			await EditFileAsync(fileSystem, RemotePath.Combine(directory, name), name, createNew: true, "Could not create " + name);
		}
		finally
		{
			EndOperation();
		}
	}

	/// <summary>
	/// The browser's side of an edit: the folder is listed again once the file may have changed (after a save, and after
	/// any attempt to create one), and a file that could not be opened shows an alert that offers root after a permission
	/// error, which runs the same edit again with the root file system.
	/// </summary>
	private async Task EditFileAsync(IRemoteFileSystem fileSystem, string path, string name, bool createNew, string failureTitle)
	{
		CancellationToken token = ConnectionToken;
		RemoteTextEditResult result = await Editor.EditAsync(
			new RemoteTextEditRequest { FileSystem = fileSystem, Path = path, DisplayName = name, CreateNew = createNew },
			token);
		if (token.IsCancellationRequested || !BelongsToConnection(fileSystem))
		{
			return;
		}

		if (result is { Outcome: RemoteTextEditOutcome.Failed, Error: { } error })
		{
			ShowAlert(failureTitle, error, fileSystem.IsElevated ? null : root => EditFileAsync(root, path, name, createNew, failureTitle));
		}

		if (result.Outcome == RemoteTextEditOutcome.Saved || createNew)
		{
			await RefreshAsync(path);
		}
	}
}
