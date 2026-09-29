using Microsoft.AspNetCore.Components.Web;
using Moka.Red.ContextMenu;
using Moka.Red.Core.Interactions;
using Moka.Red.Icons;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.UI.Common.Icons;
using Mokaterm.UI.FileBrowser.Browsing;
using Mokaterm.UI.FileBrowser.Properties;

namespace Mokaterm.UI.FileBrowser;

// Pointer, keyboard and context menu input.
public partial class RemoteFileBrowser
{
	// Set by an inner handler and read by an outer one for the same browser event: Blazor dispatches innermost first.
	private bool _cellClickHandled;
	private bool _entryMenuOpened;

	private void OnEntryClick(RemoteFileEntry entry, MouseEventArgs args)
	{
		_cellClickHandled = true;
		if (args.ShiftKey)
		{
			_selection.SelectRange(_view, entry.Path);
		}
		else if (args.CtrlKey || args.MetaKey)
		{
			_selection.Toggle(entry.Path);
		}
		else
		{
			_selection.SelectOnly(entry.Path);
		}

		UpdateSelectedItems();
	}

	// The table's row click carries no modifier keys. Clicks on cell content were handled above with theirs, so only a
	// click that lands on a cell border selects from here.
	private void OnRowClick(RemoteFileEntry entry)
	{
		if (_cellClickHandled)
		{
			_cellClickHandled = false;
			return;
		}

		_selection.SelectOnly(entry.Path);
		UpdateSelectedItems();
	}

	internal Task HandleKeyCommandAsync(string command, bool shift, int pageRows) => RunSafeAsync(async () =>
	{
		if (_disposed || !IsActive || !CanBrowse || _editingPath)
		{
			return;
		}

		switch (command)
		{
			case "open":
				await OpenSelectionAsync();
				break;
			case "toggle":
				ToggleFocused();
				break;
			case "up":
				await GoUpAsync();
				break;
			case "back":
				await GoBackAsync();
				break;
			case "forward":
				await GoForwardAsync();
				break;
			case "refresh":
				await RefreshAsync();
				break;
			case "rename":
				if (FocusedOrOnlySelected() is { } entry)
				{
					await RenameAsync(entry);
				}

				break;
			case "edit":
				if (FocusedOrOnlySelected() is { IsDirectoryLike: false } file)
				{
					await EditAsync(file);
				}

				break;
			case "delete":
				await DeleteAsync(SelectedEntries);
				break;
			case "selectAll":
				_selection.SelectAll(_view);
				UpdateSelectedItems();
				break;
			case "clear":
				_selection.Clear();
				UpdateSelectedItems();
				break;
			case "previous":
				MoveFocusBy(-1, shift);
				break;
			case "next":
				MoveFocusBy(1, shift);
				break;
			case "pageUp":
				MoveFocusBy(-Math.Max(1, pageRows), shift);
				break;
			case "pageDown":
				MoveFocusBy(Math.Max(1, pageRows), shift);
				break;
			case "first":
				MoveFocusTo(0, shift);
				break;
			case "last":
				MoveFocusTo(_view.Count - 1, shift);
				break;
			case "editPath":
				BeginPathEdit();
				break;
			case "filter":
				_focusSelector = "[data-filter]";
				break;
			default:
				return;
		}

		StateHasChanged();
	});

	/// <summary>Jumps to the next name starting with the typed letters. Repeating one letter cycles through its names.</summary>
	internal Task HandleTypeAheadAsync(string prefix) => RunSafeAsync(() =>
	{
		if (_disposed || !IsActive || !CanBrowse || _view.Count == 0 || string.IsNullOrEmpty(prefix))
		{
			return Task.CompletedTask;
		}

		bool repeated = prefix.Length > 1 && prefix.All(character => char.ToUpperInvariant(character) == char.ToUpperInvariant(prefix[0]));
		string needle = repeated ? prefix[..1] : prefix;
		int start = Math.Max(0, _view.IndexOf(_selection.Focus));
		int offset = needle.Length == 1 && _view.IndexOf(_selection.Focus) >= 0 ? 1 : 0;
		for (int step = 0; step < _view.Count; step++)
		{
			int index = (start + offset + step) % _view.Count;
			if (_view.Entries[index].Name.StartsWith(needle, StringComparison.OrdinalIgnoreCase))
			{
				MoveFocusTo(index, extend: false);
				StateHasChanged();
				break;
			}
		}

		return Task.CompletedTask;
	});

	internal Task HandleEntryDropAsync(string sourcePath, string targetDirectory) => RunSafeAsync(async () =>
	{
		if (_disposed || !CanBrowse || _view.Find(sourcePath) is not { } source || _view.Find(targetDirectory) is not { IsDirectoryLike: true })
		{
			return;
		}

		List<RemoteFileEntry> moving = _selection.Contains(sourcePath) ? SelectedEntries : [source];
		// A folder cannot move into itself or below itself.
		moving.RemoveAll(entry => RemotePath.IsSameOrInside(targetDirectory, entry.Path));
		if (moving.Count > 0)
		{
			await MoveAsync(moving, targetDirectory);
		}
	});

	/// <summary>The browser crossed one of <see cref="WidthSteps"/>; <paramref name="step"/> counts the steps it reaches.</summary>
	internal Task HandleWidthChangedAsync(int step) => RunSafeAsync(() =>
	{
		BrowserWidth width = (BrowserWidth)Math.Clamp(step, (int)BrowserWidth.Narrow, (int)BrowserWidth.Wide);
		if (!_disposed && width != _width)
		{
			_width = width;
			StateHasChanged();
		}

		return Task.CompletedTask;
	});

	private RemoteFileEntry? FocusedOrOnlySelected()
	{
		List<RemoteFileEntry> selected = SelectedEntries;
		return selected.Count == 1 ? selected[0] : _view.Find(_selection.Focus);
	}

	private void ToggleFocused()
	{
		if (_selection.Focus is { } focus && _view.IndexOf(focus) >= 0)
		{
			_selection.Toggle(focus);
			UpdateSelectedItems();
		}
	}

	private void MoveFocusBy(int delta, bool extend)
	{
		if (_view.Count == 0)
		{
			return;
		}

		int current = _view.IndexOf(_selection.Focus);
		int target = current < 0 ? (delta > 0 ? 0 : _view.Count - 1) : current + delta;
		MoveFocusTo(target, extend);
	}

	private void MoveFocusTo(int index, bool extend)
	{
		if (_view.Count == 0)
		{
			return;
		}

		int target = Math.Clamp(index, 0, _view.Count - 1);
		string path = _view.Entries[target].Path;
		if (extend)
		{
			_selection.SelectRange(_view, path);
		}
		else
		{
			_selection.SelectOnly(path);
		}

		UpdateSelectedItems();
		_scrollToIndex = target;
	}

	private void ShowEntryMenu(MokaItemContextMenuArgs<RemoteFileEntry> args)
	{
		_entryMenuOpened = true;
		RemoteFileEntry entry = args.Item;
		if (!_selection.Contains(entry.Path))
		{
			_selection.SelectOnly(entry.Path);
			UpdateSelectedItems();
		}

		ContextMenu.Show(args.MouseEvent, BuildEntryMenu(entry, SelectedEntries));
	}

	// The narrow toolbar's overflow button: the same actions as a right-click on the list background.
	private void ShowActionsMenu(MouseEventArgs args)
	{
		if (CanBrowse)
		{
			ContextMenu.Show(args, BuildBackgroundMenu());
		}
	}

	// Right-clicks on rows bubble here after the table showed the entry menu.
	private void OnBackgroundContextMenu(MouseEventArgs args)
	{
		if (_entryMenuOpened)
		{
			_entryMenuOpened = false;
			return;
		}

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
		bool canOpenTerminal = single && entry.IsDirectoryLike && Session.Terminal is not null;

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
			OnClick = () => RenameAsync(entry),
		});
		items.Add(new MokaContextMenuItem { Text = "Delete", Icon = MokaIcons.Action.Delete, Shortcut = "Del", OnClick = () => DeleteAsync(selected) });

		items.Add(new MokaContextMenuItem
		{
			Text = single ? "Copy path" : "Copy paths",
			Icon = MokaIcons.Content.Copy,
			DividerBefore = true,
			OnClick = () => CopyAsync(string.Join('\n', selected.Select(item => item.Path)), single ? "Path copied" : "Paths copied"),
		});
		items.Add(new MokaContextMenuItem
		{
			Text = single ? "Copy name" : "Copy names",
			Icon = MokaIcons.Content.Copy,
			OnClick = () => CopyAsync(string.Join('\n', selected.Select(item => item.Name)), single ? "Name copied" : "Names copied"),
		});

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
		items.Add(new MokaContextMenuItem { Text = "Refresh", Icon = MokaIcons.Action.Refresh, Shortcut = "F5", OnClick = () => RefreshAsync() });
		if (Session.Terminal is not null)
		{
			items.Add(new MokaContextMenuItem { Text = "Open terminal here", Icon = MokatermIcons.Terminal, OnClick = () => OpenTerminalHereAsync(_path) });
		}

		items.Add(new MokaContextMenuItem
		{
			Text = "Show hidden files",
			Icon = MokaIcons.Toggle.Eye,
			Checked = ShowHidden,
			DividerBefore = true,
			OnClick = ToggleHiddenAsync,
		});

		if (CanElevate)
		{
			items.Add(new MokaContextMenuItem { Text = "Run as root", Icon = MokatermIcons.Root, Checked = IsElevated, OnClick = () => SwitchModeAsync(!IsElevated) });
		}

		return items;
	}
}
