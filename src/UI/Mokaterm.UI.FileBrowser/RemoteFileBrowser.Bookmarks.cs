using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Moka.Red.ContextMenu;
using Moka.Red.Icons;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.UI.Common.Formatting;
using Mokaterm.UI.Common.Icons;
using Mokaterm.UI.FileBrowser.Bookmarks;

namespace Mokaterm.UI.FileBrowser;

// Folder bookmarks of the session's login, kept in the vault.
public partial class RemoteFileBrowser
{
	private IReadOnlyList<string> _bookmarks = [];
	private bool _bookmarksLoaded;

	[Inject]
	private FolderBookmarkStore Bookmarks { get; set; } = default!;

	/// <summary>A quick connection has no saved login to keep bookmarks under.</summary>
	private bool CanBookmark => !Session.IsTransient;

	private bool IsBookmarked => _bookmarks.Contains(_path, StringComparer.Ordinal);

	private async Task LoadBookmarksAsync()
	{
		if (_disposed || !CanBookmark)
		{
			return;
		}

		try
		{
			_bookmarks = await Bookmarks.ListAsync(Session.Connection.Id, _lifetime.Token);
			_bookmarksLoaded = true;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			// A locked vault or a damaged document leaves the menu empty; using a bookmark says why.
			Logger.LogDebug("The folder bookmarks could not be read: {Error}", LogSafe.Describe(ex));
		}

		StateHasChanged();
	}

	private void OnBookmarksChanged() => _ = RunSafeAsync(LoadBookmarksAsync);

	private async Task ShowBookmarksMenuAsync(MouseEventArgs args)
	{
		if (!_bookmarksLoaded)
		{
			await LoadBookmarksAsync();
		}

		ContextMenu.Show(args, BuildBookmarksMenu());
	}

	private List<MokaContextMenuItem> BuildBookmarksMenu()
	{
		string current = _path;
		bool bookmarked = IsBookmarked;
		List<MokaContextMenuItem> items =
		[
			new MokaContextMenuItem
			{
				Text = bookmarked ? "Remove this folder's bookmark" : "Bookmark this folder",
				Icon = bookmarked ? MokaIcons.Toggle.StarOutline : MokaIcons.Toggle.Star,
				Disabled = !CanBrowse,
				OnClick = () => bookmarked ? RemoveBookmarkAsync(current) : AddBookmarkAsync(current),
			},
		];

		if (_bookmarks.Count == 0)
		{
			items.Add(new MokaContextMenuItem { Text = "No bookmarks yet", Disabled = true, DividerBefore = true });
			return items;
		}

		for (int index = 0; index < _bookmarks.Count; index++)
		{
			string path = _bookmarks[index];
			items.Add(new MokaContextMenuItem
			{
				Text = path,
				Icon = MokatermIcons.Folder,
				Checked = path == current,
				DividerBefore = index == 0,
				OnClick = () => NavigateAsync(path),
			});
		}

		items.Add(new MokaContextMenuItem
		{
			Text = "Remove a bookmark",
			Icon = MokaIcons.Action.Delete,
			DividerBefore = true,
			Children = [.. _bookmarks.Select(path => new MokaContextMenuItem { Text = path, OnClick = () => RemoveBookmarkAsync(path) })],
		});
		return items;
	}

	private Task AddBookmarkAsync(string path) =>
		ChangeBookmarksAsync(token => Bookmarks.AddAsync(Session.Connection.Id, path, token), "bookmark this folder");

	private Task RemoveBookmarkAsync(string path) =>
		ChangeBookmarksAsync(token => Bookmarks.RemoveAsync(Session.Connection.Id, path, token), "remove the bookmark");

	private async Task ChangeBookmarksAsync(Func<CancellationToken, Task<IReadOnlyList<string>>> change, string action)
	{
		if (_disposed || !CanBookmark)
		{
			return;
		}

		try
		{
			_bookmarks = await change(_lifetime.Token);
			_bookmarksLoaded = true;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Logger.LogDebug("Changing the folder bookmarks failed: {Error}", LogSafe.Describe(ex));
			Interaction.Notify(NoticeSeverity.Warning, VaultFailure.Describe(ex, action));
		}

		StateHasChanged();
	}
}
