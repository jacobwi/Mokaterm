using System.Globalization;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.FileBrowser.Browsing;
using Mokaterm.UI.FileBrowser.Transfers;

namespace Mokaterm.UI.FileBrowser;

// Copying, cutting and pasting entries on the server, and duplicating them where they are.
public partial class RemoteFileBrowser
{
	private EntryClipboard? _clipboard;

	private bool CanCopyOnServer => HasFeature(RemoteFileSystemFeatures.Copy);

	/// <summary>A cut pastes as a move, which every file system can do; a copy needs the server to copy.</summary>
	private bool CanPaste => _clipboard is { } clipboard && (clipboard.IsCut || CanCopyOnServer);

	private string? ClipboardText => _clipboard is { } clipboard
		? string.Create(
			CultureInfo.CurrentCulture,
			$"{clipboard.Entries.Count:N0} {(clipboard.Entries.Count == 1 ? "item" : "items")} {(clipboard.IsCut ? "cut" : "copied")}")
		: null;

	/// <summary>Holds <paramref name="entries"/> for the next paste. Nothing moves until then.</summary>
	private void HoldEntries(List<RemoteFileEntry> entries, bool cut)
	{
		if (entries.Count == 0)
		{
			return;
		}

		if (!cut && !CanCopyOnServer)
		{
			Interaction.Notify(NoticeSeverity.Warning, "This connection cannot copy on the server. Cut and paste moves files instead.");
			return;
		}

		_clipboard = new EntryClipboard(entries, cut);

		// Also reached from the context menu, whose click belongs to another component, so the status bar is redrawn here.
		StateHasChanged();
	}

	/// <summary>Pastes what was copied or cut into <paramref name="targetDirectory"/>: copies on the server, or moves.</summary>
	private async Task PasteAsync(string targetDirectory)
	{
		if (_clipboard is not { } clipboard || !CanPaste || !BeginOperation())
		{
			return;
		}

		try
		{
			if (clipboard.IsCut)
			{
				await PasteMovedAsync(clipboard, targetDirectory);
			}
			else
			{
				await CopyIntoAsync(clipboard.Entries, targetDirectory, askAboutConflicts: true);
			}
		}
		finally
		{
			EndOperation();
		}
	}

	/// <summary>Copies the entries beside themselves under names of their own, <c>report copy.txt</c>.</summary>
	private async Task DuplicateAsync(List<RemoteFileEntry> entries)
	{
		if (entries.Count == 0 || !CanCopyOnServer || !BeginOperation())
		{
			return;
		}

		try
		{
			await CopyIntoAsync(entries, _path, askAboutConflicts: false);
		}
		finally
		{
			EndOperation();
		}
	}

	private async Task CopyIntoAsync(IReadOnlyList<RemoteFileEntry> entries, string targetDirectory, bool askAboutConflicts)
	{
		if (_fileSystem is not { } fileSystem || !CanCopyOnServer)
		{
			return;
		}

		List<RemoteFileEntry> sources = [.. entries.Where(entry => !PastePlan.IsIntoItself(entry, targetDirectory))];
		if (sources.Count < entries.Count)
		{
			ShowAlert("Could not paste", "A folder cannot be copied into itself.", retryAsRoot: null);
			if (sources.Count == 0)
			{
				return;
			}
		}

		// The folder is read again rather than taken from the listing on screen, which may be older than what is there now.
		CancellationToken token = ConnectionToken;
		HashSet<string> existing;
		try
		{
			existing = [.. (await fileSystem.ListAsync(targetDirectory, token)).Select(entry => entry.Name)];
		}
		catch (Exception ex)
		{
			if (!token.IsCancellationRequested)
			{
				ShowAlert("Could not paste into " + targetDirectory, ex, retryAsRoot: null);
			}

			return;
		}

		PasteConflictChoice choice = PasteConflictChoice.KeepBoth;
		List<string> conflicts = PastePlan.Conflicts(sources, targetDirectory, existing);
		if (askAboutConflicts && conflicts.Count > 0)
		{
			if (await Dialogs.AskPasteConflictAsync(conflicts, targetDirectory, fileSystem.IsElevated) is not { } answer || _disposed)
			{
				return;
			}

			choice = answer;
		}

		List<PasteStep> steps = PastePlan.Build(sources, targetDirectory, existing, choice);
		HashSet<int> done = [];
		await RunChangeAsync(
			ChangeFailureTitle("copy", sources),
			(target, cancellationToken) => EntryCopier.CopyAsync(target, steps, done, cancellationToken),
			select: RemotePath.Normalize(targetDirectory) == _path ? steps[0].Destination : null);
	}

	private async Task PasteMovedAsync(EntryClipboard clipboard, string targetDirectory)
	{
		// A cut pasted where it already is changes nothing, and a folder cannot go inside itself.
		List<RemoteFileEntry> moving = [.. clipboard.Entries.Where(entry => !PastePlan.IsIn(entry, targetDirectory) && !PastePlan.IsIntoItself(entry, targetDirectory))];
		if (moving.Count == 0)
		{
			return;
		}

		OverwriteBehavior behavior = Settings.Get<FileTransferSettings>().Overwrite;
		await RunChangeAsync(
			ChangeFailureTitle("move", moving),
			(fileSystem, token) => EntryMover.MoveAsync(fileSystem, moving, targetDirectory, new OverwriteResolver(Interaction, behavior, moving.Count > 1), token),
			then: () =>
			{
				// What was cut is somewhere else now, so pasting it again would find nothing.
				if (ReferenceEquals(_clipboard, clipboard))
				{
					_clipboard = null;
				}

				return Task.CompletedTask;
			});
	}
}
