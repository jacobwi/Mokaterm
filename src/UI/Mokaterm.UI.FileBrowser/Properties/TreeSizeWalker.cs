using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.UI.FileBrowser.Properties;

/// <summary>Adds up what a selection holds by listing every folder in it, one listing at a time.</summary>
internal static class TreeSizeWalker
{
	/// <summary>Where a walk stops, so a tree with millions of entries cannot keep the server busy for hours.</summary>
	public const long DefaultLimit = 250_000;

	/// <summary>
	/// Counts the selected entries that are not folders and everything inside the selected folders, without following links.
	/// A folder that cannot be listed is counted as unreadable and skipped. <paramref name="progress"/> hears the totals
	/// after every listing.
	/// </summary>
	public static async Task<TreeSize> MeasureAsync(
		IRemoteFileSystem fileSystem,
		IReadOnlyList<RemoteFileEntry> roots,
		Action<TreeSize>? progress,
		long limit,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(fileSystem);
		ArgumentNullException.ThrowIfNull(roots);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

		Tally tally = new();
		Stack<string> folders = new();
		foreach (RemoteFileEntry root in roots)
		{
			if (root.Kind == RemoteEntryKind.Directory)
			{
				folders.Push(root.Path);
			}
			else
			{
				tally.Add(root);
			}
		}

		while (folders.TryPop(out string? folder))
		{
			cancellationToken.ThrowIfCancellationRequested();
			IReadOnlyList<RemoteFileEntry> children;
			try
			{
				children = await fileSystem.ListAsync(folder, cancellationToken);
			}
			catch (RemoteFileSystemException ex) when (ex.Kind is RemoteFileErrorKind.PermissionDenied or RemoteFileErrorKind.NotFound)
			{
				// Unreadable, or deleted while the walk ran. The totals become a lower bound, and the count says so.
				tally.UnreadableFolders++;
				progress?.Invoke(tally.ToSize(truncated: false));
				continue;
			}

			foreach (RemoteFileEntry child in children)
			{
				if (child.Kind == RemoteEntryKind.Directory)
				{
					folders.Push(child.Path);
				}

				tally.Add(child);
				if (tally.Items >= limit)
				{
					TreeSize truncated = tally.ToSize(truncated: true);
					progress?.Invoke(truncated);
					return truncated;
				}
			}

			progress?.Invoke(tally.ToSize(truncated: false));
		}

		return tally.ToSize(truncated: false);
	}

	private sealed class Tally
	{
		private long _files;
		private long _folders;
		private long _links;
		private long _bytes;

		public long UnreadableFolders { get; set; }

		public long Items => _files + _folders + _links;

		public void Add(RemoteFileEntry entry)
		{
			switch (entry.Kind)
			{
				case RemoteEntryKind.Directory:
					_folders++;
					break;
				case RemoteEntryKind.SymbolicLink:
					_links++;
					break;
				case RemoteEntryKind.File:
					_files++;
					_bytes += Math.Max(entry.Size, 0);
					break;
				default:
					_files++;
					break;
			}
		}

		public TreeSize ToSize(bool truncated) => new(_files, _folders, _links, _bytes, UnreadableFolders, truncated);
	}
}
