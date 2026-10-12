using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.FileBrowser.Compare;
using Mokaterm.UI.FileBrowser.Transfers;

namespace Mokaterm.UI.FileBrowser;

// Finding files by name, and comparing a folder with a local one to upload what differs.
public partial class RemoteFileBrowser
{
	// The last search, so the dialog opens with what was typed the time before.
	private RemoteSearchQuery? _lastSearch;

	/// <summary>
	/// Searches below <paramref name="folder"/> on the file system the browser shows, user or root, and goes to the result
	/// the user chose: into it for a folder, to its folder with it selected for anything else.
	/// </summary>
	private async Task FindFilesAsync(string folder)
	{
		if (_fileSystem is not { } fileSystem || !BeginOperation())
		{
			return;
		}

		try
		{
			RemoteFileEntry? found = await Dialogs.FindFilesAsync(
				folder,
				_lastSearch ?? new RemoteSearchQuery { Pattern = "", IncludeHidden = ShowHidden },
				(text, cancellationToken) => ResolveSearchFolderAsync(fileSystem, text, cancellationToken),
				(root, query, progress, cancellationToken) =>
				{
					_lastSearch = query;
					return WhileConnectedAsync(token => fileSystem.SearchAsync(root, query, progress, token), cancellationToken);
				});

			// A reconnect while the dialog was open brings a new file system, and the result's path may mean nothing there.
			if (found is not null && !_disposed && ReferenceEquals(fileSystem, _fileSystem))
			{
				await GoToFoundAsync(found);
			}
		}
		finally
		{
			EndOperation();
		}
	}

	private async Task GoToFoundAsync(RemoteFileEntry found)
	{
		if (found.IsDirectoryLike)
		{
			await NavigateAsync(found.Path);
			return;
		}

		// A filter typed earlier could hide what the search found.
		if (_filter.Length > 0)
		{
			_filter = "";
			RebuildView();
		}

		string folder = RemotePath.GetParent(found.Path);
		await NavigateAsync(folder, select: found.Path);
		if (_path == folder && found.IsHidden && !ShowHidden)
		{
			Interaction.Notify(NoticeSeverity.Info, found.Name + " is hidden. Show hidden files to see it.");
		}
	}

	private async Task<string> ResolveSearchFolderAsync(IRemoteFileSystem fileSystem, string text, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return _path;
		}

		using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, ConnectionToken);
		return await ResolveTypedPathAsync(fileSystem, text, _path, linked.Token);
	}

	/// <summary>
	/// Picks a local folder, compares it with <paramref name="remoteFolder"/> and uploads what the user ticked, replacing
	/// what the server has without asking again: the compare was the question. Nothing on the server is deleted.
	/// </summary>
	private async Task CompareWithLocalFolderAsync(string remoteFolder)
	{
		if (!LocalFiles.CanPickFolders || _fileSystem is not { } fileSystem || !BeginOperation())
		{
			return;
		}

		IReadOnlyList<LocalFileItem> picked = [];
		IReadOnlyList<LocalFileItem>? upload = null;
		try
		{
			try
			{
				picked = await LocalFiles.PickFolderAsync(ConnectionToken);
			}
			catch (Exception ex)
			{
				if (!ConnectionToken.IsCancellationRequested)
				{
					ShowAlert("Could not pick a folder", ex, retryAsRoot: null);
				}

				return;
			}

			if (picked.Count == 0 || _disposed || !ReferenceEquals(fileSystem, _fileSystem))
			{
				return;
			}

			upload = await Dialogs.CompareFoldersAsync(
				picked,
				FolderComparison.FolderName(picked),
				remoteFolder,
				TransferOrigin.Format(fileSystem.IsElevated ? "root" : fileSystem.UserName, Source.Location, remoteFolder),
				fileSystem.IsElevated,
				(progress, cancellationToken) => WhileConnectedAsync(
					token => RemoteTreeWalker.SearchAsync(fileSystem, remoteFolder, FolderComparison.RemoteQuery, progress, token),
					cancellationToken),
				fileSystem.ServerChecksum is { } algorithm
					? (rows, progress, cancellationToken) => SameContentAsync(fileSystem, algorithm, rows, progress, cancellationToken)
					: null);
		}
		finally
		{
			EndOperation();
			if (upload is not { Count: > 0 })
			{
				await LocalFiles.ReleaseAsync(picked);
			}
		}

		if (upload is not { Count: > 0 })
		{
			return;
		}

		// The upload waits for its transfers and for the chance to retry them as root, so the picked files stay readable
		// until then. The browser itself is free again meanwhile.
		try
		{
			if (!_disposed && BelongsToConnection(fileSystem))
			{
				await UploadAsync(fileSystem, remoteFolder, upload, OverwriteBehavior.Overwrite);
			}
		}
		finally
		{
			await LocalFiles.ReleaseAsync(picked);
		}
	}

	/// <summary>
	/// The rows whose two copies hash the same. One at a time, since each asks the server to read a whole file. A file
	/// either side cannot hash keeps the status it had rather than ending the check.
	/// </summary>
	private async Task<IReadOnlySet<string>> SameContentAsync(
		IRemoteFileSystem fileSystem,
		ChecksumAlgorithm algorithm,
		IReadOnlyList<(string Path, LocalFileItem Local, RemoteFileEntry Remote)> rows,
		IProgress<int> progress,
		CancellationToken cancellationToken)
	{
		using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, ConnectionToken);
		CancellationToken token = linked.Token;
		HashSet<string> same = new(StringComparer.Ordinal);
		int done = 0;
		foreach ((string path, LocalFileItem local, RemoteFileEntry remote) in rows)
		{
			token.ThrowIfCancellationRequested();
			if (local.OpenReadAsync is { } open)
			{
				try
				{
					string? server = await fileSystem.ComputeChecksumAsync(remote.Path, token);
					if (server is null)
					{
						// The server turned out unable to; nothing further will hash either.
						break;
					}

					await using Stream content = await open(token);
					if (server == await FileChecksums.ComputeAsync(content, algorithm, token))
					{
						same.Add(path);
					}
				}
				catch (Exception ex) when (ex is RemoteFileSystemException or IOException)
				{
					Logger.LogDebug("A file was left out of the content check: {Error}", LogSafe.Describe(ex));
				}
			}

			progress.Report(++done);
		}

		return same;
	}

	/// <summary>Runs a walk that also stops when the connection goes away or the browser closes.</summary>
	private async IAsyncEnumerable<RemoteFileEntry> WhileConnectedAsync(
		Func<CancellationToken, IAsyncEnumerable<RemoteFileEntry>> walk,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, ConnectionToken);
		await foreach (RemoteFileEntry entry in walk(linked.Token))
		{
			yield return entry;
		}
	}
}
