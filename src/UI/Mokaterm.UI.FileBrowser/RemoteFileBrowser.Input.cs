using Microsoft.AspNetCore.Components.Web;
using Moka.Red.ContextMenu;
using Moka.Red.Data.FileBrowser;
using Moka.Red.Icons;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.UI.Common.Icons;
using Mokaterm.UI.FileBrowser.Browsing;
using Mokaterm.UI.FileBrowser.Properties;

namespace Mokaterm.UI.FileBrowser;

// Pointer, keyboard and context menu input.
public partial class RemoteFileBrowser
{
	internal Task HandleKeyCommandAsync(string command, string? cursor) => RunSafeAsync(async () =>
	{
		if (_disposed || !IsActive || !CanBrowse || _editingPath)
		{
			return;
		}

		switch (command)
		{
			case "open":
				await OpenSelectionAsync(cursor);
				break;
			case "up":
				await GoUpAsync();
				break;
			case "refresh":
				await RefreshAsync();
				break;
			case "rename":
				if (FocusedOrOnlySelected(cursor) is { } entry)
				{
					BeginRename(entry);
				}

				break;
			case "edit":
				if (FocusedOrOnlySelected(cursor) is { IsDirectoryLike: false } file)
				{
					await EditAsync(file);
				}

				break;
			case "delete":
				await DeleteAsync(SelectedEntries);
				break;
			case "clear":
				// A name that was refused keeps its field open after the focus has left it, where its own Escape cannot reach.
				// Through the parameter, because the browser's own Escape would also forget the keyboard position.
				_renamingPath = null;
				_selected = NothingSelected;
				break;
			case "editPath":
				BeginPathEdit();
				break;
			case "filter":
				_focusSelector = "[data-moka-file-filter]";
				break;
			case "find":
				await FindFilesAsync(_path);
				break;
			case "newFile":
				await CreateFileAsync();
				break;
			case "copy":
				HoldEntries(SelectedEntries, cut: false);
				break;
			case "cut":
				HoldEntries(SelectedEntries, cut: true);
				break;
			case "paste":
				await PasteAsync(_path);
				break;
			case "duplicate":
				await DuplicateAsync(SelectedEntries);
				break;
			case "copyPath":
				await CopyPathsAsync(SelectedEntries);
				break;
			default:
				return;
		}

		StateHasChanged();
	});

	/// <summary>Jumps to the next name starting with the typed letters. Repeating one letter cycles through its names.</summary>
	internal Task HandleTypeAheadAsync(string prefix, string? cursor) => RunSafeAsync(async () =>
	{
		if (_disposed || !IsActive || !CanBrowse || _view.Count == 0 || string.IsNullOrEmpty(prefix))
		{
			return;
		}

		bool repeated = prefix.Length > 1 && prefix.All(character => char.ToUpperInvariant(character) == char.ToUpperInvariant(prefix[0]));
		string needle = repeated ? prefix[..1] : prefix;
		string? from = cursor ?? (_selected.Count == 1 ? _selected.First() : null);
		int start = Math.Max(0, _view.IndexOf(from));
		int offset = needle.Length == 1 && _view.IndexOf(from) >= 0 ? 1 : 0;
		for (int step = 0; step < _view.Count; step++)
		{
			RemoteFileEntry candidate = _view.Entries[(start + offset + step) % _view.Count];
			if (candidate.Name.StartsWith(needle, StringComparison.OrdinalIgnoreCase))
			{
				await RevealAsync(candidate.Path);
				break;
			}
		}
	});

	private async Task MoveDraggedAsync(IReadOnlyList<RemoteFileEntry> entries, string targetDirectory)
	{
		// A folder cannot move into itself or below itself.
		List<RemoteFileEntry> moving = [.. entries.Where(entry => !RemotePath.IsSameOrInside(targetDirectory, entry.Path))];
		if (!_disposed && CanBrowse && moving.Count > 0)
		{
			await MoveAsync(moving, targetDirectory);
		}
	}

	/// <summary>The browser crossed one of <see cref="WidthSteps"/>; <paramref name="step"/> counts the steps it reaches.</summary>
	internal Task HandleWidthChangedAsync(int step) => RunSafeAsync(() =>
	{
		BrowserWidth width = (BrowserWidth)Math.Clamp(step, (int)BrowserWidth.Narrow, (int)BrowserWidth.Roomy);
		if (!_disposed && width != _width)
		{
			_width = width;
			StateHasChanged();
		}

		return Task.CompletedTask;
	});

	private RemoteFileEntry? FocusedOrOnlySelected(string? cursor)
	{
		List<RemoteFileEntry> selected = SelectedEntries;
		return selected.Count == 1 ? selected[0] : _view.Find(cursor);
	}

	private List<MokaContextMenuItem> ItemMenu(MokaFileMenuContext<RemoteFileEntry> context) =>
		BuildEntryMenu(context.Items[0], [.. context.Items]);

	private List<MokaContextMenuItem> BackgroundMenu(MokaFileMenuContext<RemoteFileEntry> context) =>
		CanBrowse ? BuildBackgroundMenu() : [];

	// The narrow toolbar's overflow button: the same actions as a right-click on the list background.
	private void ShowActionsMenu(MouseEventArgs args)
	{
		if (CanBrowse)
		{
			ContextMenu.Show(args, BuildBackgroundMenu());
		}
	}

	private List<MokaContextMenuItem> BuildEntryMenu(RemoteFileEntry entry, List<RemoteFileEntry> selected)
	{
		bool single = selected.Count == 1;
		bool hasFiles = selected.Exists(item => !item.IsDirectoryLike);
		bool hasFolders = selected.Exists(item => item.IsDirectoryLike);
		bool canOpenTerminal = single && entry.IsDirectoryLike && Source.Terminal is not null;

		List<MokaContextMenuItem> items = [];
		if (single && entry.IsDirectoryLike)
		{
			items.Add(new MokaContextMenuItem { Text = "Open", Icon = MokatermIcons.FolderOpen, Shortcut = "Enter", OnClick = () => OpenEntryAsync(entry) });
		}
		else
		{
			items.Add(new MokaContextMenuItem
			{
				Text = "Download",
				Icon = MokatermIcons.FileDownload,
				Shortcut = "Enter",
				Disabled = !hasFiles,
				OnClickSync = () => Download(selected),
			});
		}

		if (hasFolders || !single)
		{
			items.Add(new MokaContextMenuItem { Text = "Download as zip", Icon = MokaIcons.Action.Download, OnClickSync = () => DownloadZip(selected) });
		}

		// A link to a folder is searched and compared through the link, which the user pointed at on purpose.
		if (single && entry.IsDirectoryLike)
		{
			items.Add(new MokaContextMenuItem { Text = "Find files...", Icon = MokaIcons.Action.Search, DividerBefore = true, OnClick = () => FindFilesAsync(entry.Path) });
			if (LocalFiles.CanPickFolders)
			{
				items.Add(new MokaContextMenuItem { Text = "Compare with local folder...", Icon = MokaIcons.Content.Columns, OnClick = () => CompareWithLocalFolderAsync(entry.Path) });
			}
		}

		if (single && !entry.IsDirectoryLike)
		{
			// F4 is the edit key every file manager has used since Norton Commander.
			items.Add(new MokaContextMenuItem
			{
				Text = "Edit",
				Icon = MokatermIcons.File,
				Shortcut = "F4",
				OnClick = () => EditAsync(entry),
			});
		}

		items.Add(new MokaContextMenuItem
		{
			Text = "Rename",
			Icon = MokaIcons.Action.Edit,
			Shortcut = "F2",
			Disabled = !single,
			DividerBefore = true,
			OnClickSync = () => BeginRename(entry),
		});
		items.Add(new MokaContextMenuItem { Text = "Delete", Icon = MokaIcons.Action.Delete, Shortcut = "Del", OnClick = () => DeleteAsync(selected) });

		AddClipboardItems(items, entry, selected);
		AddArchiveItems(items, entry, selected);

		items.Add(new MokaContextMenuItem
		{
			Text = single ? "Copy path" : "Copy paths",
			Icon = MokaIcons.Content.Copy,
			Shortcut = "Ctrl+Shift+C",
			DividerBefore = true,
			OnClick = () => CopyPathsAsync(selected),
		});
		items.Add(new MokaContextMenuItem
		{
			Text = single ? "Copy name" : "Copy names",
			Icon = MokaIcons.Content.Copy,
			OnClick = () => CopyAsync(string.Join('\n', selected.Select(item => item.Name)), single ? "Name copied" : "Names copied"),
		});
		if (single && !entry.IsDirectoryLike && _fileSystem?.ServerChecksum is { } algorithm)
		{
			items.Add(new MokaContextMenuItem
			{
				Text = "Copy " + FileChecksums.Name(algorithm),
				Icon = MokaIcons.Content.Copy,
				OnClick = () => CopyChecksumAsync(entry),
			});
		}

		// Both open the properties dialog at their section. Links are never changed, so a selection of links has nothing to edit.
		bool onlyLinks = selected.TrueForAll(item => item.Kind == RemoteEntryKind.SymbolicLink);
		bool permissions = HasFeature(RemoteFileSystemFeatures.Permissions);
		if (permissions)
		{
			items.Add(new MokaContextMenuItem
			{
				Text = "Permissions...",
				Icon = MokatermIcons.Lock,
				Disabled = onlyLinks,
				DividerBefore = true,
				OnClick = () => ShowPropertiesAsync(selected, PropertiesSection.Permissions),
			});
		}

		if (HasFeature(RemoteFileSystemFeatures.Ownership))
		{
			items.Add(new MokaContextMenuItem
			{
				Text = "Change owner...",
				Icon = MokatermIcons.User,
				Disabled = onlyLinks,
				DividerBefore = !permissions,
				OnClick = () => ShowPropertiesAsync(selected, PropertiesSection.Ownership),
			});
		}

		if (canOpenTerminal)
		{
			items.Add(new MokaContextMenuItem { Text = "Open terminal here", Icon = MokatermIcons.Terminal, DividerBefore = true, OnClick = () => OpenTerminalHereAsync(entry.Path) });
		}

		items.Add(new MokaContextMenuItem
		{
			Text = "Properties",
			Icon = MokaIcons.Status.Info,
			DividerBefore = !canOpenTerminal,
			OnClick = () => ShowPropertiesAsync(selected, PropertiesSection.General),
		});
		return items;
	}

	private List<MokaContextMenuItem> BuildBackgroundMenu()
	{
		List<MokaContextMenuItem> items =
		[
			new MokaContextMenuItem { Text = "Upload files", Icon = MokatermIcons.FileUpload, OnClick = PickAndUploadFilesAsync },
		];

		if (LocalFiles.CanPickFolders)
		{
			items.Add(new MokaContextMenuItem { Text = "Upload folder", Icon = MokaIcons.Action.Upload, OnClick = PickAndUploadFolderAsync });
		}

		items.Add(new MokaContextMenuItem { Text = "New folder", Icon = MokatermIcons.FolderPlus, DividerBefore = true, OnClick = CreateFolderAsync });
		items.Add(new MokaContextMenuItem { Text = "New file", Icon = MokatermIcons.FilePlus, Shortcut = "Shift+F4", OnClick = CreateFileAsync });
		if (CanCopyOnServer || _clipboard is not null)
		{
			string here = _path;
			items.Add(new MokaContextMenuItem { Text = "Paste", Icon = MokaIcons.Content.Paste, Shortcut = "Ctrl+V", Disabled = !CanPaste, OnClick = () => PasteAsync(here) });
		}

		items.Add(new MokaContextMenuItem { Text = "Refresh", Icon = MokaIcons.Action.Refresh, Shortcut = "F5", OnClick = () => RefreshAsync() });
		if (Source.Terminal is not null)
		{
			items.Add(new MokaContextMenuItem { Text = "Open terminal here", Icon = MokatermIcons.Terminal, OnClick = () => OpenTerminalHereAsync(_path) });
		}

		items.Add(new MokaContextMenuItem
		{
			Text = "Copy folder path",
			Icon = MokaIcons.Content.Copy,
			Shortcut = "Ctrl+Shift+C",
			DividerBefore = true,
			OnClick = () => CopyPathsAsync([]),
		});
		if (CanBookmark)
		{
			string here = _path;
			bool bookmarked = IsBookmarked;
			items.Add(new MokaContextMenuItem
			{
				Text = bookmarked ? "Remove this folder's bookmark" : "Bookmark this folder",
				Icon = bookmarked ? MokaIcons.Toggle.StarOutline : MokaIcons.Toggle.Star,
				OnClick = () => bookmarked ? RemoveBookmarkAsync(here) : AddBookmarkAsync(here),
			});
		}

		items.Add(new MokaContextMenuItem
		{
			Text = "Find files...",
			Icon = MokaIcons.Action.Search,
			Shortcut = "Ctrl+Shift+F",
			DividerBefore = true,
			OnClick = () => FindFilesAsync(_path),
		});
		if (LocalFiles.CanPickFolders)
		{
			items.Add(new MokaContextMenuItem { Text = "Compare with local folder...", Icon = MokaIcons.Content.Columns, OnClick = () => CompareWithLocalFolderAsync(_path) });
		}

		items.Add(new MokaContextMenuItem
		{
			Text = "Show hidden files",
			Icon = MokaIcons.Toggle.Eye,
			Checked = ShowHidden,
			DividerBefore = true,
			OnClick = ToggleHiddenAsync,
		});
		items.Add(new MokaContextMenuItem { Text = "Show as tiles", Icon = MokaIcons.Navigation.Grid, Checked = IsTiles, OnClickSync = ToggleTiles });
		if (CanShowDetails)
		{
			items.Add(new MokaContextMenuItem { Text = "Show details", Icon = MokaIcons.Navigation.PanelRight, Checked = _detailsOpen, OnClickSync = ToggleDetails });
		}

		if (CanFollowTerminal)
		{
			items.Add(new MokaContextMenuItem
			{
				Text = "Follow the terminal's folder",
				Icon = MokaIcons.Content.Link,
				Checked = FollowTerminal,
				OnClick = ToggleFollowTerminalAsync,
			});
		}

		if (CanElevate)
		{
			items.Add(new MokaContextMenuItem { Text = "Run as root", Icon = MokatermIcons.Root, Checked = IsElevated, OnClick = () => SwitchModeAsync(!IsElevated) });
		}

		return items;
	}

	// Copy and Duplicate need the server to copy, so a connection that cannot (FTP) shows neither. Cut is a move, which
	// every file system can do, and Paste follows whatever is held.
	private void AddClipboardItems(List<MokaContextMenuItem> items, RemoteFileEntry entry, List<RemoteFileEntry> selected)
	{
		bool canCopy = CanCopyOnServer;
		if (canCopy)
		{
			items.Add(new MokaContextMenuItem
			{
				Text = "Copy",
				Icon = MokaIcons.Content.Copy,
				Shortcut = "Ctrl+C",
				DividerBefore = true,
				OnClickSync = () => HoldEntries(selected, cut: false),
			});
		}

		items.Add(new MokaContextMenuItem
		{
			Text = "Cut",
			Icon = MokatermIcons.Cut,
			Shortcut = "Ctrl+X",
			DividerBefore = !canCopy,
			OnClickSync = () => HoldEntries(selected, cut: true),
		});
		if (CanPaste)
		{
			string here = _path;
			items.Add(new MokaContextMenuItem { Text = "Paste", Icon = MokaIcons.Content.Paste, Shortcut = "Ctrl+V", OnClick = () => PasteAsync(here) });
			if (selected.Count == 1 && entry.IsDirectoryLike)
			{
				items.Add(new MokaContextMenuItem { Text = "Paste into folder", Icon = MokaIcons.Content.Paste, OnClick = () => PasteAsync(entry.Path) });
			}
		}

		if (canCopy)
		{
			items.Add(new MokaContextMenuItem { Text = "Duplicate", Icon = MokaIcons.Content.Copy, Shortcut = "Ctrl+D", OnClick = () => DuplicateAsync(selected) });
		}
	}

	private void AddArchiveItems(List<MokaContextMenuItem> items, RemoteFileEntry entry, List<RemoteFileEntry> selected)
	{
		if (!CanUseArchives)
		{
			return;
		}

		items.Add(new MokaContextMenuItem { Text = "Compress...", Icon = MokatermIcons.Archive, DividerBefore = true, OnClick = () => CompressAsync(selected) });
		if (selected.Count == 1 && !entry.IsDirectoryLike && RemoteArchives.KindOf(entry.Name) != RemoteArchiveKind.None)
		{
			items.Add(new MokaContextMenuItem { Text = "Extract here", Icon = MokatermIcons.Archive, OnClick = () => ExtractHereAsync(entry) });
		}
	}
}
