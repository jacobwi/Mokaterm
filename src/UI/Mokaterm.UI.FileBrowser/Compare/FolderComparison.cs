using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Platform;

namespace Mokaterm.UI.FileBrowser.Compare;

/// <summary>
/// Lines a picked local folder up against a remote one by relative path and says which copies differ. Paths compare
/// exactly, case included: the server is POSIX, so <c>Readme.md</c> and <c>README.md</c> are two files there, and an
/// upload writes the local name as it is.
/// </summary>
internal static class FolderComparison
{
	/// <summary>The most remote files a compare reads, which also bounds the rows the dialog holds.</summary>
	public const int MaxRemoteFiles = 50_000;

	/// <summary>
	/// The most files one content check hashes. Each costs the server a read of the file and this side a read of the local
	/// copy, which on the web host is the browser sending it over.
	/// </summary>
	public const int MaxContentChecks = 500;

	/// <summary>Every file under the remote folder, hidden ones included because a local pick has them too.</summary>
	public static RemoteSearchQuery RemoteQuery { get; } = new()
	{
		Pattern = "",
		Kinds = RemoteSearchKinds.Files,
		IncludeHidden = true,
		MaxResults = MaxRemoteFiles,
	};

	/// <summary>The picked folder's own name, which every picked path starts with.</summary>
	public static string FolderName(IReadOnlyList<LocalFileItem> picked)
	{
		ArgumentNullException.ThrowIfNull(picked);
		foreach (LocalFileItem item in picked)
		{
			if (item.RelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries) is [{ } first, ..])
			{
				return first;
			}
		}

		return "";
	}

	/// <summary>
	/// The picked files keyed by their path below the picked folder. Pickers root every path at the folder's own name
	/// (<c>site/css/app.css</c>), which is dropped here (<c>css/app.css</c>) because the remote folder stands for it.
	/// </summary>
	public static Dictionary<string, LocalFileItem> LocalFiles(IReadOnlyList<LocalFileItem> picked)
	{
		ArgumentNullException.ThrowIfNull(picked);
		Dictionary<string, LocalFileItem> files = new(StringComparer.Ordinal);
		foreach (LocalFileItem item in picked)
		{
			if (item.IsDirectory || item.OpenReadAsync is null)
			{
				continue;
			}

			// The same segments the upload keeps, so a row's path is where its upload lands.
			string[] segments = [.. item.RelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Where(RemotePath.IsValidName)];
			string? key = segments.Length switch
			{
				> 1 => string.Join('/', segments[1..]),
				1 => segments[0],
				_ => RemotePath.IsValidName(item.Name) ? item.Name : null,
			};

			if (key is not null)
			{
				files.TryAdd(key, item);
			}
		}

		return files;
	}

	/// <summary>The files a walk of <paramref name="root"/> found, keyed by their path below it. Folders are left out.</summary>
	public static Dictionary<string, RemoteFileEntry> RemoteFiles(string root, IEnumerable<RemoteFileEntry> entries)
	{
		ArgumentNullException.ThrowIfNull(entries);
		Dictionary<string, RemoteFileEntry> files = new(StringComparer.Ordinal);
		foreach (RemoteFileEntry entry in entries)
		{
			if (!entry.IsDirectoryLike && RemotePath.GetRelativePath(root, entry.Path) is { Length: > 0 } key)
			{
				files.TryAdd(key, entry);
			}
		}

		return files;
	}

	/// <summary>One row per relative path found on either side, in ordinal path order.</summary>
	public static List<FolderCompareRow> Compare(
		IReadOnlyDictionary<string, LocalFileItem> local,
		IReadOnlyDictionary<string, RemoteFileEntry> remote,
		FolderCompareOptions options)
	{
		ArgumentNullException.ThrowIfNull(local);
		ArgumentNullException.ThrowIfNull(remote);
		ArgumentNullException.ThrowIfNull(options);

		SortedSet<string> paths = new(local.Keys, StringComparer.Ordinal);
		paths.UnionWith(remote.Keys);
		List<FolderCompareRow> rows = new(paths.Count);
		foreach (string path in paths)
		{
			LocalFileItem? localFile = local.GetValueOrDefault(path);
			RemoteFileEntry? remoteFile = remote.GetValueOrDefault(path);
			FolderCompareStatus status = options.SameContent?.Contains(path) == true && localFile is not null && remoteFile is not null
				? FolderCompareStatus.Same
				: Classify(localFile, remoteFile, options);
			rows.Add(new FolderCompareRow(path, status, localFile, remoteFile));
		}

		return rows;
	}

	public static FolderCompareStatus Classify(LocalFileItem? local, RemoteFileEntry? remote, FolderCompareOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		if (local is null)
		{
			return remote is null
				? throw new ArgumentException("A row needs a file on at least one side.", nameof(local))
				: FolderCompareStatus.OnlyRemote;
		}

		if (remote is null)
		{
			return FolderCompareStatus.OnlyLocal;
		}

		if (!options.IgnoreTimes && local.LastModified is { } localTime && remote.LastModified is { } remoteTime)
		{
			TimeSpan difference = localTime - remoteTime;
			if (difference > options.TimeTolerance)
			{
				return FolderCompareStatus.LocalNewer;
			}

			if (difference < -options.TimeTolerance)
			{
				return FolderCompareStatus.RemoteNewer;
			}
		}

		// A size only one side knows says nothing, so it counts as the same.
		return local.Length is { } length && length != remote.Size ? FolderCompareStatus.SizeDiffers : FolderCompareStatus.Same;
	}

	/// <summary>
	/// Whether the dialog ticks a row for upload before the user touches it: files the server lacks, newer local copies,
	/// and copies of another size unless the local one is known to be older. When the remote walk did not see everything
	/// (a cap, or folders it could not read), "only local" may only mean "not seen", and replacing what was not seen
	/// without asking is not something to start with.
	/// </summary>
	public static bool IsPreselected(FolderCompareRow row, FolderCompareOptions options, bool remoteComplete)
	{
		ArgumentNullException.ThrowIfNull(row);
		ArgumentNullException.ThrowIfNull(options);
		return row.CanUpload && row.Status switch
		{
			FolderCompareStatus.OnlyLocal => remoteComplete,
			FolderCompareStatus.LocalNewer => true,
			FolderCompareStatus.SizeDiffers => !IsLocalOlder(row, options),
			_ => false,
		};
	}

	/// <summary>
	/// The rows a content check can settle: both copies have the same size and only their times differ, which is a file
	/// touched or copied without being changed as often as it is a real edit. A size difference already says the contents
	/// differ, so those are not asked about.
	/// </summary>
	public static List<FolderCompareRow> ContentCandidates(IEnumerable<FolderCompareRow> rows)
	{
		ArgumentNullException.ThrowIfNull(rows);
		return [.. rows
			.Where(row => row.Status is FolderCompareStatus.LocalNewer or FolderCompareStatus.RemoteNewer
				&& row is { Local: { Length: { } length, OpenReadAsync: not null }, Remote: { } remote }
				&& length == remote.Size)
			.Take(MaxContentChecks)];
	}

	/// <summary>The local copies of <paramref name="rows"/>, renamed to their paths below the remote folder, for the upload.</summary>
	public static List<LocalFileItem> UploadItems(IEnumerable<FolderCompareRow> rows)
	{
		ArgumentNullException.ThrowIfNull(rows);
		List<LocalFileItem> items = [];
		foreach (FolderCompareRow row in rows)
		{
			if (row.CanUpload && row.Local is { } local)
			{
				items.Add(local with { RelativePath = row.RelativePath });
			}
		}

		return items;
	}

	// Comparing sizes only still leaves the times known, and a size change is no reason to put an older copy over a newer one.
	private static bool IsLocalOlder(FolderCompareRow row, FolderCompareOptions options) =>
		row.Local?.LastModified is { } localTime
		&& row.Remote?.LastModified is { } remoteTime
		&& remoteTime - localTime > options.TimeTolerance;
}
