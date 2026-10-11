using System.Runtime.CompilerServices;

namespace Mokaterm.Abstractions.FileSystem;

/// <summary>
/// The walk behind the default <see cref="IRemoteFileSystem.SearchAsync"/>, built from listings alone. It is public for
/// callers that rely on how this walk behaves (the folder compare reads a whole tree through it), whatever search a
/// protocol brings of its own.
/// </summary>
public static class RemoteTreeWalker
{
	/// <summary>
	/// Lists <paramref name="root"/> and the folders below it breadth first, so shallow matches come first, and yields what
	/// <paramref name="query"/> matches as each listing arrives. Links are returned but never followed, which is what keeps
	/// a link to a parent folder from walking in circles. A folder below the root that cannot be read (permission denied,
	/// or deleted meanwhile) is counted and skipped; the root itself failing throws. Cancellation is checked before every
	/// listing.
	/// </summary>
	public static async IAsyncEnumerable<RemoteFileEntry> SearchAsync(
		IRemoteFileSystem fileSystem,
		string root,
		RemoteSearchQuery query,
		IProgress<RemoteSearchProgress>? progress = null,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(fileSystem);
		ArgumentNullException.ThrowIfNull(root);
		ArgumentNullException.ThrowIfNull(query);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(query.MaxResults);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(query.MaxFolders);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(query.MaxDepth);

		string start = RemotePath.Normalize(root);
		Queue<(string Path, int Depth)> pending = new();
		pending.Enqueue((start, 1));
		int visited = 0;
		int unreadable = 0;
		int found = 0;

		// Folders the caps kept out of the queue. The queue never holds more than the folder cap leaves room for, so a
		// tree with a million folders costs no more memory than one that fits.
		bool leftDeeper = false;
		bool leftOver = false;
		bool resultsCut = false;
		while (pending.TryDequeue(out (string Path, int Depth) folder))
		{
			cancellationToken.ThrowIfCancellationRequested();
			visited++;
			progress?.Report(new RemoteSearchProgress
			{
				FoldersVisited = visited,
				CurrentFolder = folder.Path,
				UnreadableFolders = unreadable,
				Found = found,
			});

			IReadOnlyList<RemoteFileEntry> listing;
			try
			{
				listing = await fileSystem.ListAsync(folder.Path, cancellationToken);
			}
			catch (RemoteFileSystemException ex) when (folder.Path != start && ex.Kind is RemoteFileErrorKind.PermissionDenied or RemoteFileErrorKind.NotFound)
			{
				unreadable++;
				continue;
			}

			// Servers list in no particular order; by name, the results read the same on every run.
			RemoteFileEntry[] children = [.. listing.OrderBy(child => child.Name, StringComparer.Ordinal)];
			for (int index = 0; index < children.Length; index++)
			{
				RemoteFileEntry child = children[index];
				if (child.Kind == RemoteEntryKind.Directory && (query.IncludeHidden || !child.IsHidden))
				{
					if (folder.Depth >= query.MaxDepth)
					{
						leftDeeper = true;
					}
					else if (visited + pending.Count >= query.MaxFolders)
					{
						leftOver = true;
					}
					else
					{
						pending.Enqueue((child.Path, folder.Depth + 1));
					}
				}

				if (!query.Matches(child))
				{
					continue;
				}

				found++;
				yield return child;
				if (found >= query.MaxResults)
				{
					resultsCut = index < children.Length - 1 || pending.Count > 0 || leftDeeper || leftOver;
					break;
				}
			}

			if (found >= query.MaxResults)
			{
				break;
			}
		}

		progress?.Report(new RemoteSearchProgress
		{
			FoldersVisited = visited,
			UnreadableFolders = unreadable,
			Found = found,
			Limit = resultsCut ? RemoteSearchLimit.Results
				: leftOver ? RemoteSearchLimit.Folders
				: leftDeeper ? RemoteSearchLimit.Depth
				: RemoteSearchLimit.None,
		});
	}
}
