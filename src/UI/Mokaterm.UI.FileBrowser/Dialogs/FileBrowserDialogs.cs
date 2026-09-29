using Moka.Red.Feedback.Dialog;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.UI.FileBrowser.Properties;

namespace Mokaterm.UI.FileBrowser.Dialogs;

/// <summary>
/// Opens the file browser's dialogs and returns their results typed. Call only from the browser itself: awaiting a
/// service dialog from inside another service dialog waits forever.
/// </summary>
internal sealed class FileBrowserDialogs
{
	private readonly IMokaDialogService _dialogs;

	public FileBrowserDialogs(IMokaDialogService dialogs) => _dialogs = dialogs;

	/// <returns>The new name, or null when cancelled.</returns>
	public async Task<string?> AskNameAsync(
		string title,
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
			Small);
		return result as string;
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
			Small);
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
				options.Size = MokaDialogSize.Medium;
				options.ShowCloseButton = false;
			});
		return result as PropertiesChange;
	}

	/// <returns>The account to upload as, or null when cancelled.</returns>
	public async Task<UploadRunAs?> ConfirmUploadAsync(
		int fileCount,
		int folderCount,
		long? totalBytes,
		string directory,
		string host,
		string userName,
		bool supportsElevation,
		bool asRoot)
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
			},
			Small);
		return result is UploadRunAs runAs ? runAs : null;
	}

	/// <param name="save">Writes the file and returns the message to show when it failed, or null once it is saved.</param>
	/// <param name="confirmDiscard">Asked before throwing away unsaved edits.</param>
	/// <returns>True once a save went through.</returns>
	public async Task<bool> EditFileAsync(
		string fileName,
		string path,
		string content,
		string facts,
		Func<string, Task<string?>> save,
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
				parameters[nameof(FileEditDialog.ConfirmDiscard)] = confirmDiscard;
			},
			options =>
			{
				options.Size = MokaDialogSize.FullScreen;
				// The dialog asks about unsaved edits from its own Cancel, which a header X and Escape would skip.
				options.ShowCloseButton = false;
				options.CloseOnEscape = false;
			});
		return result is true;
	}

	// The dialogs render their own Cancel buttons. Without the header X, the dialog's first field gets the initial focus.
	private static void Small(MokaDialogOptions options)
	{
		options.Size = MokaDialogSize.Small;
		options.ShowCloseButton = false;
	}
}
