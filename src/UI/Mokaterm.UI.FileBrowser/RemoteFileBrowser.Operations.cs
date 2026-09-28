using System.Globalization;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Transfers;
using Mokaterm.UI.Common.Interop;
using Mokaterm.UI.Common.Platform;
using Mokaterm.UI.FileBrowser.Browsing;
using Mokaterm.UI.FileBrowser.Dialogs;
using Mokaterm.UI.FileBrowser.Properties;
using Mokaterm.UI.FileBrowser.Transfers;

namespace Mokaterm.UI.FileBrowser;

// User and root switching, errors, file operations and transfers.
public partial class RemoteFileBrowser
{
	// Dialog-driven operations run one at a time; a second Delete press must not queue a second dialog.
	private bool _operationRunning;

	private async Task OnModeChangedAsync(string value) => await SwitchModeAsync(value == RootMode);

	/// <summary>
	/// Switches the browser between the user and root file systems and lists the same folder again. Returns the file
	/// system now in use, or null when the switch failed and the browser stayed as it was.
	/// </summary>
	private async Task<IRemoteFileSystem?> SwitchModeAsync(bool elevated)
	{
		if (_fileSystem is null)
		{
			return null;
		}

		if (_fileSystem.IsElevated == elevated)
		{
			return _fileSystem;
		}

		IRemoteFileSystem? target = await GetFileSystemAsync(elevated);
		if (target is null)
		{
			// Re-render so the User / Root control drops the choice that did not happen.
			StateHasChanged();
			return null;
		}

		_fileSystem = target;
		CloseAlert();
		await RefreshAsync();
		return target;
	}

	/// <summary>
	/// The user or root file system of the current connection, without switching the browser. The root one opens on
	/// first use, which may ask for the sudo password; a refusal shows an alert and returns null.
	/// </summary>
	private async Task<IRemoteFileSystem?> GetFileSystemAsync(bool elevated)
	{
		if (!elevated)
		{
			return _userFileSystem;
		}

		if (_rootFileSystem is not null)
		{
			return _rootFileSystem;
		}

		if (_feature is not { SupportsElevation: true } feature)
		{
			return null;
		}

		CancellationToken token = ConnectionToken;
		_switchingMode = true;
		StateHasChanged();
		try
		{
			IRemoteFileSystem root = await feature.OpenElevatedAsync(token);
			if (token.IsCancellationRequested)
			{
				return null;
			}

			_rootFileSystem = root;
			return root;
		}
		catch (OperationCanceledException)
		{
			// The sudo prompt was cancelled, or the connection went away.
			return null;
		}
		catch (Exception ex)
		{
			if (!token.IsCancellationRequested)
			{
				ShowAlert("Could not switch to root", ex, retryAsRoot: null);
			}

			return null;
		}
		finally
		{
			_switchingMode = false;
		}
	}

	private BrowserAlert ShowAlert(string title, Exception exception, Func<IRemoteFileSystem, Task>? retryAsRoot)
	{
		Logger.LogDebug("A file browser action failed: {Error}", LogSafe.Describe(exception));
		bool offerRoot = retryAsRoot is not null && RemoteErrorText.IsPermissionDenied(exception) && CanElevate && !IsElevated;
		return ShowAlert(title, RemoteErrorText.Describe(exception), offerRoot ? retryAsRoot : null);
	}

	private BrowserAlert ShowAlert(string title, string message, Func<IRemoteFileSystem, Task>? retryAsRoot)
	{
		_alert?.Close();
		BrowserAlert alert = new() { Title = title, Message = message, RetryAsRoot = retryAsRoot };
		_alert = alert;
		StateHasChanged();
		return alert;
	}

	private void CloseAlert()
	{
		_alert?.Close();
		_alert = null;
	}

	private void DismissAlert(BrowserAlert alert)
	{
		if (ReferenceEquals(_alert, alert))
		{
			CloseAlert();
		}
		else
		{
			alert.Close();
		}
	}

	private async Task RetryAsRootAsync(BrowserAlert alert)
	{
		if (!ReferenceEquals(_alert, alert) || alert.RetryAsRoot is not { } retry)
		{
			return;
		}

		_alert = null;
		try
		{
			if (await SwitchModeAsync(elevated: true) is { IsElevated: true } root)
			{
				await retry(root);
			}
		}
		finally
		{
			alert.Close();
		}
	}

	private bool BeginOperation()
	{
		if (_operationRunning || !CanBrowse)
		{
			return false;
		}

		_operationRunning = true;
		return true;
	}

	private void EndOperation()
	{
		_operationRunning = false;
		_restoreFocus = true;
		StateHasChanged();
	}

	private bool BelongsToConnection(IRemoteFileSystem fileSystem) =>
		ReferenceEquals(fileSystem, _userFileSystem) || ReferenceEquals(fileSystem, _rootFileSystem);

	/// <summary>The account of <paramref name="fileSystem"/>, for transfers that ask the session for its file system when they run.</summary>
	private TransferFileSystem ForTransfers(IRemoteFileSystem fileSystem) => new(fileSystem, Session, _protocolSession);

	/// <summary>
	/// Applies a change with <paramref name="fileSystem"/>, or the browser's current one, and lists the folder again, also
	/// after a partial failure. A failure shows an alert, and a permission error offers to run the change again as root.
	/// </summary>
	private async Task RunChangeAsync(
		string failureTitle,
		Func<IRemoteFileSystem, CancellationToken, Task> change,
		string? select = null,
		IRemoteFileSystem? fileSystem = null)
	{
		if ((fileSystem ?? _fileSystem) is not { } target)
		{
			return;
		}

		CancellationToken token = ConnectionToken;
		bool succeeded = false;
		_busy++;
		StateHasChanged();
		try
		{
			await change(target, token);
			succeeded = true;
		}
		catch (Exception ex)
		{
			if (!token.IsCancellationRequested && BelongsToConnection(target))
			{
				ShowAlert(failureTitle, ex, target.IsElevated ? null : root => RunChangeAsync(failureTitle, change, select, root));
			}
		}
		finally
		{
			_busy--;
		}

		if (!token.IsCancellationRequested && BelongsToConnection(target))
		{
			await RefreshAsync(succeeded ? select : null);
		}
	}

	private async Task OpenSelectionAsync()
	{
		List<RemoteFileEntry> selected = SelectedEntries;
		if (selected.Count > 1 && selected.TrueForAll(entry => !entry.IsDirectoryLike))
		{
			Download(selected);
			return;
		}

		if ((_view.Find(_selection.Focus) ?? selected.FirstOrDefault()) is { } target)
		{
			await OpenEntryAsync(target);
		}
	}

	/// <summary>Opens a folder or a link to one, and downloads anything else.</summary>
	private async Task OpenEntryAsync(RemoteFileEntry entry)
	{
		if (_fileSystem is not { } fileSystem)
		{
			return;
		}

		if (entry.IsDirectoryLike)
		{
			await NavigateAsync(entry.Path);
			return;
		}

		if (entry.Kind == RemoteEntryKind.SymbolicLink && entry.LinkTargetKind is null)
		{
			// The listing could not tell what the link points at, so ask the server before choosing.
			CancellationToken token = ConnectionToken;
			try
			{
				string resolved = await fileSystem.ResolvePathAsync(entry.Path, token);
				if (await fileSystem.StatAsync(resolved, token) is { IsDirectoryLike: true })
				{
					await NavigateAsync(entry.Path);
					return;
				}
			}
			catch (Exception ex)
			{
				if (!token.IsCancellationRequested)
				{
					ShowAlert("Could not open " + entry.Name, ex, retryAsRoot: null);
				}

				return;
			}
		}

		Download([entry]);
	}

	private void Download(IReadOnlyList<RemoteFileEntry> entries)
	{
		if (_fileSystem is not { } fileSystem)
		{
			return;
		}

		TransferOrigin origin = Origin;
		TransferFileSystem source = ForTransfers(fileSystem);
		foreach (RemoteFileEntry entry in entries)
		{
			if (entry.Kind == RemoteEntryKind.Other)
			{
				ShowAlert("Could not download " + entry.Name, "Devices, sockets and pipes cannot be downloaded.", retryAsRoot: null);
			}
			else if (!entry.IsDirectoryLike)
			{
				try
				{
					Transfers.EnqueueDownload(source, origin, entry);
				}
				catch (Exception ex)
				{
					ShowAlert("Could not download " + entry.Name, ex, retryAsRoot: null);
					return;
				}
			}
		}
	}

	private void DownloadZip(List<RemoteFileEntry> entries)
	{
		if (_fileSystem is not { } fileSystem || entries.Count == 0)
		{
			return;
		}

		string name = entries.Count == 1 ? entries[0].Name
			: RemotePath.IsRoot(_path) ? "download"
			: RemotePath.GetName(_path);
		try
		{
			Transfers.EnqueueZipDownload(ForTransfers(fileSystem), Origin, entries, name + ".zip");
		}
		catch (Exception ex)
		{
			ShowAlert("Could not start the download", ex, retryAsRoot: null);
		}
	}

	private async Task CreateFolderAsync()
	{
		if (!BeginOperation())
		{
			return;
		}

		try
		{
			string directory = _path;
			HashSet<string> taken = TakenNames(except: null);
			string? name = await Dialogs.AskNameAsync("New folder", "Folder name", UniqueName("New folder", taken), "Create", taken, selectBaseName: false);
			if (name is null)
			{
				return;
			}

			string path = RemotePath.Combine(directory, name);
			await RunChangeAsync("Could not create " + name, (fileSystem, token) => fileSystem.CreateDirectoryAsync(path, token).AsTask(), select: path);
		}
		finally
		{
			EndOperation();
		}
	}

	private async Task RenameAsync(RemoteFileEntry entry)
	{
		if (!BeginOperation())
		{
			return;
		}

		try
		{
			string? name = await Dialogs.AskNameAsync(
				"Rename",
				entry.IsDirectoryLike ? "Folder name" : "File name",
				entry.Name,
				"Rename",
				TakenNames(except: entry.Name),
				selectBaseName: !entry.IsDirectoryLike);
			if (name is null || name == entry.Name)
			{
				return;
			}

			string target = RemotePath.Combine(RemotePath.GetParent(entry.Path), name);
			await RunChangeAsync(
				"Could not rename " + entry.Name,
				(fileSystem, token) => fileSystem.RenameAsync(entry.Path, target, overwrite: false, token).AsTask(),
				select: target);
		}
		finally
		{
			EndOperation();
		}
	}

	private async Task DeleteAsync(List<RemoteFileEntry> entries)
	{
		if (entries.Count == 0 || !BeginOperation())
		{
			return;
		}

		try
		{
			if (Settings.Get<FileTransferSettings>().ConfirmDelete && !await Dialogs.ConfirmDeleteAsync(entries, IsElevated))
			{
				return;
			}

			// Keep the keyboard position: the row after the deleted ones becomes the focus.
			int focusIndex = _view.IndexOf(_selection.Focus);
			string title = entries.Count == 1
				? "Could not delete " + entries[0].Name
				: string.Create(CultureInfo.CurrentCulture, $"Could not delete {entries.Count} items");
			await RunChangeAsync(title, async (fileSystem, token) =>
			{
				foreach (RemoteFileEntry entry in entries)
				{
					token.ThrowIfCancellationRequested();
					try
					{
						// Links are removed themselves, never what they point at.
						await fileSystem.DeleteAsync(entry.Path, recursive: entry.Kind == RemoteEntryKind.Directory, token);
					}
					catch (RemoteFileSystemException ex) when (ex.Kind == RemoteFileErrorKind.NotFound)
					{
						// Already gone, for example when a retry as root repeats the whole list.
					}
				}
			});

			if (_selection.Count == 0 && focusIndex >= 0 && _view.Count > 0)
			{
				MoveFocusTo(Math.Min(focusIndex, _view.Count - 1), extend: false);
			}
		}
		finally
		{
			EndOperation();
		}
	}

	/// <summary>
	/// Shows the properties of <paramref name="entries"/> and runs what the user changed there, as the logged-in user or
	/// as root. Like a root upload, root opens without switching the browser.
	/// </summary>
	private async Task ShowPropertiesAsync(List<RemoteFileEntry> entries, PropertiesSection section)
	{
		if (entries.Count == 0 || _fileSystem is not { } listing || !BeginOperation())
		{
			return;
		}

		try
		{
			CancellationToken token = ConnectionToken;
			bool listsAccounts = (listing.Features & RemoteFileSystemFeatures.Accounts) != 0;
			PropertiesChange? change = await Dialogs.ShowPropertiesAsync(
				entries,
				section,
				_userFileSystem?.UserName ?? listing.UserName,
				listing.IsElevated,
				CanElevate,
				listing.Features,
				listsAccounts ? cancellationToken => ListAccountsAsync(listing, cancellationToken) : null,
				(progress, cancellationToken) => MeasureAsync(listing, entries, progress, cancellationToken));
			if (change is null || _disposed || token.IsCancellationRequested)
			{
				return;
			}

			if (await GetFileSystemAsync(change.AsRoot) is { } fileSystem)
			{
				await RunChangeAsync(
					ChangeFailureTitle(change.Action, entries),
					(target, cancellationToken) => change.ApplyAsync(target, entries, cancellationToken),
					fileSystem: fileSystem);
			}
		}
		finally
		{
			EndOperation();
		}
	}

	private async Task<RemoteAccounts> ListAccountsAsync(IRemoteFileSystem fileSystem, CancellationToken cancellationToken)
	{
		using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, ConnectionToken);
		try
		{
			return await fileSystem.ListAccountsAsync(linked.Token);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Logger.LogDebug(ex, "The server's users and groups could not be read");
			return RemoteAccounts.Empty;
		}
	}

	private async Task<TreeSize> MeasureAsync(IRemoteFileSystem fileSystem, IReadOnlyList<RemoteFileEntry> entries, Action<TreeSize> progress, CancellationToken cancellationToken)
	{
		using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, ConnectionToken);
		return await TreeSizeWalker.MeasureAsync(fileSystem, entries, progress, TreeSizeWalker.DefaultLimit, linked.Token);
	}

	/// <summary>Moves entries dragged onto a folder, after confirming.</summary>
	private async Task MoveAsync(List<RemoteFileEntry> entries, string targetDirectory)
	{
		if (!BeginOperation())
		{
			return;
		}

		try
		{
			string what = entries.Count == 1 ? entries[0].Name : string.Create(CultureInfo.CurrentCulture, $"{entries.Count} items");
			bool confirmed = (await Interaction.ConfirmAsync(
				new ConfirmPrompt { Title = "Move", Message = $"Move {what} into {targetDirectory}?", ConfirmText = "Move" },
				ConnectionToken)).Confirmed;
			if (!confirmed)
			{
				return;
			}

			OverwriteBehavior behavior = Settings.Get<FileTransferSettings>().Overwrite;
			await RunChangeAsync("Could not move " + what, (fileSystem, token) =>
				EntryMover.MoveAsync(fileSystem, entries, targetDirectory, new OverwriteResolver(Interaction, behavior, entries.Count > 1), token));
		}
		finally
		{
			EndOperation();
		}
	}

	private async Task CopyAsync(string text, string notice)
	{
		try
		{
			await Clipboard.WriteTextAsync(text, ConnectionToken);
			Interaction.Notify(NoticeSeverity.Success, notice);
		}
		catch (Exception ex)
		{
			// Browsers refuse clipboard writes from an unfocused page, among other reasons.
			Logger.LogDebug(ex, "Copying to the clipboard failed");
			if (!JsModule.IsTeardown(ex))
			{
				Interaction.Notify(NoticeSeverity.Warning, "Could not copy to the clipboard.");
			}
		}
	}

	/// <summary>Types <c>cd -- 'folder'</c> and Enter into the session's terminal.</summary>
	private async Task OpenTerminalHereAsync(string directory)
	{
		if (Session.Terminal is not { } terminal)
		{
			return;
		}

		if (PosixShell.ChangeDirectoryCommand(directory) is not { } command)
		{
			ShowAlert("Could not open a terminal here", "The path has control characters, which the terminal would take as key presses.", retryAsRoot: null);
			return;
		}

		try
		{
			await terminal.SendTextAsync(command, ConnectionToken);
		}
		catch (Exception ex)
		{
			if (!ConnectionToken.IsCancellationRequested)
			{
				ShowAlert("Could not send the command to the terminal", ex, retryAsRoot: null);
			}
		}
	}

	private async Task PickAndUploadFilesAsync()
	{
		if (!CanBrowse || _fileSystem is not { } fileSystem)
		{
			return;
		}

		string directory = _path;
		IReadOnlyList<LocalFileItem> items;
		try
		{
			items = await LocalFiles.PickFilesAsync(multiple: true, ConnectionToken);
		}
		catch (Exception ex)
		{
			if (!ConnectionToken.IsCancellationRequested)
			{
				ShowAlert("Could not pick files", ex, retryAsRoot: null);
			}

			return;
		}

		await UploadPickedAsync(fileSystem, directory, items);
	}

	private async Task PickAndUploadFolderAsync()
	{
		if (!CanBrowse || _fileSystem is not { } fileSystem)
		{
			return;
		}

		string directory = _path;
		IReadOnlyList<LocalFileItem> items;
		try
		{
			items = await LocalFiles.PickFolderAsync(ConnectionToken);
		}
		catch (Exception ex)
		{
			if (!ConnectionToken.IsCancellationRequested)
			{
				ShowAlert("Could not pick a folder", ex, retryAsRoot: null);
			}

			return;
		}

		await UploadPickedAsync(fileSystem, directory, items);
	}

	/// <summary>
	/// Uploads picked files, then lets the host release them. <see cref="UploadAsync"/> returns once the uploads and the
	/// chance to retry them as root are over, the same point at which dropped files are released.
	/// </summary>
	private async Task UploadPickedAsync(IRemoteFileSystem fileSystem, string directory, IReadOnlyList<LocalFileItem> items)
	{
		try
		{
			await UploadAsync(fileSystem, directory, items);
		}
		finally
		{
			await LocalFiles.ReleaseAsync(items);
		}
	}

	private Task OnDragOverChangedAsync(bool isOver) => InvokeAsync(() =>
	{
		bool show = isOver && CanBrowse && !_disposed;
		if (_dragOver != show)
		{
			_dragOver = show;
			StateHasChanged();
		}
	});

	private Task OnFilesDroppedAsync(FileDropEvent drop) => InvokeAsync(() => HandleDropAsync(drop));

	/// <summary>
	/// Uploads dropped files into the current folder. With <see cref="FileTransferSettings.ConfirmDrop"/> the user first
	/// sees what goes where and picks the account; choosing root opens the root file system without switching the view.
	/// </summary>
	private async Task HandleDropAsync(FileDropEvent drop)
	{
		if (_dragOver)
		{
			_dragOver = false;
			StateHasChanged();
		}

		if (_disposed || drop.Items.Count == 0)
		{
			return;
		}

		if (!CanBrowse || _fileSystem is not { } current)
		{
			Interaction.Notify(NoticeSeverity.Warning, "Wait for the folder to load, then drop the files again.");
			return;
		}

		string directory = _path;
		bool asRoot = current.IsElevated;
		if (Settings.Get<FileTransferSettings>().ConfirmDrop)
		{
			long? totalBytes = 0;
			foreach (LocalFileItem item in drop.Items)
			{
				if (!item.IsDirectory)
				{
					totalBytes = totalBytes is { } sum && item.Length is { } length ? sum + length : null;
				}
			}

			UploadRunAs? choice = await Dialogs.ConfirmUploadAsync(
				drop.Items.Count(item => !item.IsDirectory),
				drop.Items.Count(item => item.IsDirectory),
				totalBytes,
				directory,
				Session.Host.Address,
				_userFileSystem?.UserName ?? current.UserName,
				CanElevate,
				asRoot);
			if (choice is null || _disposed)
			{
				return;
			}

			asRoot = choice == UploadRunAs.Root;
		}

		if (await GetFileSystemAsync(asRoot) is { } fileSystem)
		{
			await UploadAsync(fileSystem, directory, drop.Items);
		}
	}

	/// <summary>
	/// Uploads into <paramref name="directory"/> and waits for the batch, then lists the folder again if it is still on
	/// screen. Files refused for lack of permission can be retried as root from the alert; this method returns only
	/// once that alert is gone, so dropped files stay readable while the retry is possible.
	/// </summary>
	private async Task UploadAsync(IRemoteFileSystem fileSystem, string directory, IReadOnlyList<LocalFileItem> items)
	{
		if (items.Count == 0 || _disposed)
		{
			return;
		}

		CancellationToken token = ConnectionToken;
		UploadBatch? batch = null;
		Exception? failure = null;
		_busy++;
		StateHasChanged();
		try
		{
			batch = await Transfers.UploadAsync(ForTransfers(fileSystem), Origin, directory, items, token);
		}
		catch (Exception ex)
		{
			failure = ex;
		}
		finally
		{
			_busy--;
			if (!_disposed)
			{
				StateHasChanged();
			}
		}

		if (batch is null)
		{
			if (failure is not null && !token.IsCancellationRequested && BelongsToConnection(fileSystem))
			{
				BrowserAlert alert = ShowAlert(
					"Could not upload to " + directory,
					failure,
					fileSystem.IsElevated ? null : root => UploadAsync(root, directory, items));
				await alert.Closed;
			}

			return;
		}

		IReadOnlyList<ITransferItem> transfers = batch.Transfers;
		if (transfers.Count == 0)
		{
			return;
		}

		// Uploads outlive the browser, so the wait is not tied to this component or its connection.
		await Transfers.WaitForCompletionAsync(transfers, CancellationToken.None);
		if (_disposed)
		{
			return;
		}

		if (CanBrowse && _path == directory)
		{
			await RefreshAsync();
		}

		IReadOnlyList<LocalFileItem> denied = batch.PermissionDenied;
		if (denied.Count > 0 && !fileSystem.IsElevated && CanElevate && BelongsToConnection(fileSystem))
		{
			string title = denied.Count == 1
				? "Could not upload " + denied[0].Name
				: string.Create(CultureInfo.CurrentCulture, $"Could not upload {denied.Count} files");
			BrowserAlert alert = ShowAlert(title, "Permission denied in " + directory + ".", root =>
			{
				Transfers.Remove(denied.Select(batch.TransferFor).OfType<ITransferItem>());
				return UploadAsync(root, directory, denied);
			});
			await alert.Closed;
		}
	}

	private HashSet<string> TakenNames(string? except)
	{
		HashSet<string> names = new(StringComparer.Ordinal);
		foreach (RemoteFileEntry entry in _entries)
		{
			if (entry.Name != except)
			{
				names.Add(entry.Name);
			}
		}

		return names;
	}

	private static string UniqueName(string name, HashSet<string> taken)
	{
		string candidate = name;
		for (int number = 2; taken.Contains(candidate); number++)
		{
			candidate = string.Create(CultureInfo.InvariantCulture, $"{name} {number}");
		}

		return candidate;
	}

	private static string ChangeFailureTitle(string action, List<RemoteFileEntry> entries) => entries.Count == 1
		? "Could not " + action + " " + entries[0].Name
		: string.Create(CultureInfo.CurrentCulture, $"Could not {action} {entries.Count} items");
}
