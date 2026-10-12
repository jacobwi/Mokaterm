using Moka.Red.Core.Icons;
using Moka.Red.Feedback.Dialog;
using Moka.Red.Icons;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Platform;
using Mokaterm.UI.Common.Icons;
using Mokaterm.UI.FileBrowser.Browsing;
using Mokaterm.UI.FileBrowser.Properties;

namespace Mokaterm.UI.FileBrowser.Dialogs;

/// <summary>
/// Opens the file browser's dialogs and returns their results typed, from the browser itself or from the editor behind
/// <see cref="Common.Editing.IRemoteTextEditor"/>. Service dialogs stack, so these open on top of a browser that is in a
/// dialog of its own.
/// </summary>
internal sealed class FileBrowserDialogs
{
	private readonly IMokaDialogService _dialogs;

	public FileBrowserDialogs(IMokaDialogService dialogs) => _dialogs = dialogs;

	/// <returns>The new name, or null when cancelled.</returns>
	public async Task<string?> AskNameAsync(
		string title,
		MokaIconDefinition icon,
		string label,
		string initialName,
		string confirmText,
		IReadOnlySet<string> takenNames,
		bool selectBaseName)
	{
		object? result = await _dialogs.ShowComponentAsync<NameDialog>(
			title,
			parameters =>
			{
				parameters[nameof(NameDialog.Label)] = label;
				parameters[nameof(NameDialog.InitialName)] = initialName;
				parameters[nameof(NameDialog.ConfirmText)] = confirmText;
				parameters[nameof(NameDialog.TakenNames)] = takenNames;
				parameters[nameof(NameDialog.SelectBaseName)] = selectBaseName;
			},
			Small(icon));
		return result as string;
	}

	/// <param name="folder">Where the search starts. The dialog lets the user change it.</param>
	/// <param name="initialQuery">The pattern and options the fields start with.</param>
	/// <param name="resolveFolder">Turns a typed folder into an absolute path.</param>
	/// <param name="search">Runs a search on the browser's file system.</param>
	/// <returns>The entry the user chose, or null when the dialog closed without one.</returns>
	public async Task<RemoteFileEntry?> FindFilesAsync(
		string folder,
		RemoteSearchQuery initialQuery,
		Func<string, CancellationToken, Task<string>> resolveFolder,
		Func<string, RemoteSearchQuery, IProgress<RemoteSearchProgress>, CancellationToken, IAsyncEnumerable<RemoteFileEntry>> search)
	{
		object? result = await _dialogs.ShowComponentAsync<FileSearchDialog>(
			"Find files",
			parameters =>
			{
				parameters[nameof(FileSearchDialog.Folder)] = folder;
				parameters[nameof(FileSearchDialog.InitialQuery)] = initialQuery;
				parameters[nameof(FileSearchDialog.ResolveFolder)] = resolveFolder;
				parameters[nameof(FileSearchDialog.Search)] = search;
			},
			options =>
			{
				options.Icon = MokaIcons.Action.Search;
				options.Size = MokaDialogSize.Large;
				options.ShowCloseButton = false;
			});
		return result as RemoteFileEntry;
	}

	/// <param name="picked">What the folder picker returned. The caller releases it.</param>
	/// <param name="destination"><c>user@host:/path</c> of <paramref name="remoteFolder"/>.</param>
	/// <param name="readRemote">Walks <paramref name="remoteFolder"/> for every file in it.</param>
	/// <param name="checkContents">Hashes both copies of the rows it gets; null when the server cannot compute checksums.</param>
	/// <returns>The files to upload, each renamed to its path below <paramref name="remoteFolder"/>, or null when cancelled.</returns>
	public async Task<IReadOnlyList<LocalFileItem>?> CompareFoldersAsync(
		IReadOnlyList<LocalFileItem> picked,
		string localName,
		string remoteFolder,
		string destination,
		bool asRoot,
		Func<IProgress<RemoteSearchProgress>, CancellationToken, IAsyncEnumerable<RemoteFileEntry>> readRemote,
		Func<IReadOnlyList<(string Path, LocalFileItem Local, RemoteFileEntry Remote)>, IProgress<int>, CancellationToken, Task<IReadOnlySet<string>>>? checkContents)
	{
		object? result = await _dialogs.ShowComponentAsync<FolderCompareDialog>(
			"Compare with local folder",
			parameters =>
			{
				parameters[nameof(FolderCompareDialog.LocalItems)] = picked;
				parameters[nameof(FolderCompareDialog.LocalName)] = localName;
				parameters[nameof(FolderCompareDialog.RemoteFolder)] = remoteFolder;
				parameters[nameof(FolderCompareDialog.Destination)] = destination;
				parameters[nameof(FolderCompareDialog.AsRoot)] = asRoot;
				parameters[nameof(FolderCompareDialog.ReadRemote)] = readRemote;
				if (checkContents is not null)
				{
					parameters[nameof(FolderCompareDialog.CheckContents)] = checkContents;
				}
			},
			options =>
			{
				options.Icon = MokatermIcons.SplitView;
				options.Size = MokaDialogSize.Large;
				options.ShowCloseButton = false;
			});
		return result as IReadOnlyList<LocalFileItem>;
	}

	public async Task<bool> ConfirmDeleteAsync(IReadOnlyList<RemoteFileEntry> entries, bool asRoot)
	{
		object? result = await _dialogs.ShowComponentAsync<DeleteDialog>(
			"Delete",
			parameters =>
			{
				parameters[nameof(DeleteDialog.Entries)] = entries;
				parameters[nameof(DeleteDialog.AsRoot)] = asRoot;
			},
			Small(MokaIcons.Action.Delete));
		return result is true;
	}

	/// <param name="userName">The logged-in account.</param>
	/// <param name="asRoot">Preselect root, as when the browser runs as root.</param>
	/// <param name="loadAccounts">Reads the server's users and groups; null when the file system cannot list them.</param>
	/// <param name="measureSize">Walks the selection for its size.</param>
	/// <returns>What to change, or null when cancelled.</returns>
	public async Task<PropertiesChange?> ShowPropertiesAsync(
		IReadOnlyList<RemoteFileEntry> entries,
		PropertiesSection section,
		string userName,
		bool asRoot,
		bool supportsElevation,
		RemoteFileSystemFeatures features,
		Func<CancellationToken, Task<RemoteAccounts>>? loadAccounts,
		Func<Action<TreeSize>, CancellationToken, Task<TreeSize>> measureSize)
	{
		object? result = await _dialogs.ShowComponentAsync<PropertiesDialog>(
			"Properties",
			parameters =>
			{
				parameters[nameof(PropertiesDialog.Entries)] = entries;
				parameters[nameof(PropertiesDialog.Section)] = section;
				parameters[nameof(PropertiesDialog.UserName)] = userName;
				parameters[nameof(PropertiesDialog.AsRoot)] = asRoot;
				parameters[nameof(PropertiesDialog.SupportsElevation)] = supportsElevation;
				parameters[nameof(PropertiesDialog.Features)] = features;
				if (loadAccounts is not null)
				{
					parameters[nameof(PropertiesDialog.LoadAccounts)] = loadAccounts;
				}

				parameters[nameof(PropertiesDialog.MeasureSize)] = measureSize;
			},
			options =>
			{
				options.Icon = MokatermIcons.Sliders;
				options.Size = MokaDialogSize.Medium;
				options.ShowCloseButton = false;
			});
		return result as PropertiesChange;
	}

	/// <param name="offerOptOut">Offers "Don't ask again", for the question the drop setting turns off.</param>
	/// <returns>The account to upload as and whether to stop asking, or null when cancelled.</returns>
	public async Task<UploadConfirmation?> ConfirmUploadAsync(
		int fileCount,
		int folderCount,
		long? totalBytes,
		string directory,
		string host,
		string userName,
		bool supportsElevation,
		bool asRoot,
		bool offerOptOut)
	{
		object? result = await _dialogs.ShowComponentAsync<UploadConfirmDialog>(
			"Upload",
			parameters =>
			{
				parameters[nameof(UploadConfirmDialog.FileCount)] = fileCount;
				parameters[nameof(UploadConfirmDialog.FolderCount)] = folderCount;
				if (totalBytes is { } bytes)
				{
					parameters[nameof(UploadConfirmDialog.TotalBytes)] = bytes;
				}

				parameters[nameof(UploadConfirmDialog.Directory)] = directory;
				parameters[nameof(UploadConfirmDialog.Host)] = host;
				parameters[nameof(UploadConfirmDialog.UserName)] = userName;
				parameters[nameof(UploadConfirmDialog.SupportsElevation)] = supportsElevation;
				parameters[nameof(UploadConfirmDialog.AsRoot)] = asRoot;
				parameters[nameof(UploadConfirmDialog.OfferOptOut)] = offerOptOut;
			},
			Small(MokatermIcons.FileUpload));
		return result as UploadConfirmation;
	}

	/// <param name="names">The pasted names <paramref name="directory"/> already holds.</param>
	/// <returns>What to do with them, or null when the paste was cancelled.</returns>
	public async Task<PasteConflictChoice?> AskPasteConflictAsync(IReadOnlyList<string> names, string directory, bool asRoot)
	{
		object? result = await _dialogs.ShowComponentAsync<PasteConflictDialog>(
			"Paste",
			parameters =>
			{
				parameters[nameof(PasteConflictDialog.Names)] = names;
				parameters[nameof(PasteConflictDialog.Directory)] = directory;
				parameters[nameof(PasteConflictDialog.AsRoot)] = asRoot;
			},
			Small(MokaIcons.Content.Paste));
		return result is PasteConflictChoice choice ? choice : null;
	}

	/// <param name="save">
	/// Writes the text, or with the flag set writes it over whatever the server has now. Answers a conflict instead of
	/// writing when the file changed on the server since it was last read.
	/// </param>
	/// <param name="acceptServerCopy">Makes the server copy the last conflict showed the one the next save compares against.</param>
	/// <param name="confirmDiscard">Asked before throwing away unsaved edits.</param>
	/// <returns>True once a save went through.</returns>
	public async Task<bool> EditFileAsync(
		string fileName,
		string path,
		string content,
		IReadOnlyList<string> facts,
		Func<string, bool, Task<Editing.FileSaveResult>> save,
		Action acceptServerCopy,
		Func<Task<bool>> confirmDiscard)
	{
		object? result = await _dialogs.ShowComponentAsync<FileEditDialog>(
			fileName,
			parameters =>
			{
				parameters[nameof(FileEditDialog.FileName)] = fileName;
				parameters[nameof(FileEditDialog.Path)] = path;
				parameters[nameof(FileEditDialog.Content)] = content;
				parameters[nameof(FileEditDialog.Facts)] = facts;
				parameters[nameof(FileEditDialog.Save)] = save;
				parameters[nameof(FileEditDialog.AcceptServerCopy)] = acceptServerCopy;
				parameters[nameof(FileEditDialog.ConfirmDiscard)] = confirmDiscard;
			},
			options =>
			{
				options.Icon = MokaIcons.Action.Edit;
				options.Size = MokaDialogSize.FullScreen;
				// The dialog asks about unsaved edits from its own Cancel, which a header X and Escape would skip.
				options.ShowCloseButton = false;
				options.CloseOnEscape = false;
			});
		return result is true;
	}

	// The dialogs render their own Cancel buttons. Without the header X, the dialog's first field gets the initial focus.
	private static Action<MokaDialogOptions> Small(MokaIconDefinition icon) => options =>
	{
		options.Size = MokaDialogSize.Small;
		options.ShowCloseButton = false;
		options.Icon = icon;
	};
}
