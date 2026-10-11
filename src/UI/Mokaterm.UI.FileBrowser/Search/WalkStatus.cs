using System.Globalization;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.UI.FileBrowser.Browsing;

namespace Mokaterm.UI.FileBrowser.Search;

/// <summary>What a status line says about a walk of a remote tree, for the search and the folder compare alike.</summary>
internal static class WalkStatus
{
	/// <summary>
	/// <c>Searching /var/log... 1,234 folders, 48 found</c>, naming the folder being read. <paramref name="extra"/> is the
	/// caller's own count, left out when null.
	/// </summary>
	public static string Running(string verb, string root, RemoteSearchProgress? progress, string? extra = null)
	{
		string folder = progress?.CurrentFolder ?? root;
		int visited = progress?.FoldersVisited ?? 0;
		List<string> counts = [];
		if (visited > 1)
		{
			counts.Add(EntryVisuals.Count(visited, "folder", "folders"));
		}

		if (extra is { Length: > 0 })
		{
			counts.Add(extra);
		}

		string running = verb + " " + folder + "...";
		return counts.Count == 0 ? running : running + " " + string.Join(", ", counts);
	}

	/// <summary><c>48 found in 2,345 folders</c>, or <c>Nothing found in 1 folder</c>.</summary>
	public static string Found(int found, int folders)
	{
		string where = EntryVisuals.Count(folders, "folder", "folders");
		return found == 0
			? "Nothing found in " + where
			: string.Create(CultureInfo.CurrentCulture, $"{found:N0} found in {where}");
	}

	/// <summary>
	/// Why the walk saw less than everything: the cap that stopped it and the folders it could not read, as sentences.
	/// Empty when it saw it all. <paramref name="resultNoun"/> names what the result cap counts (<c>result</c>, <c>file</c>).
	/// </summary>
	public static string Notes(RemoteSearchProgress progress, RemoteSearchQuery query, string resultNoun)
	{
		ArgumentNullException.ThrowIfNull(progress);
		ArgumentNullException.ThrowIfNull(query);
		List<string> notes = [];
		switch (progress.Limit)
		{
			case RemoteSearchLimit.Results:
				notes.Add(string.Create(CultureInfo.CurrentCulture, $"Stopped at the {query.MaxResults:N0} {resultNoun} limit."));
				break;
			case RemoteSearchLimit.Folders:
				notes.Add(string.Create(CultureInfo.CurrentCulture, $"Stopped at the {query.MaxFolders:N0} folder limit."));
				break;
			case RemoteSearchLimit.Depth:
				notes.Add(string.Create(CultureInfo.CurrentCulture, $"Folders more than {query.MaxDepth:N0} levels down were left out."));
				break;
			default:
				break;
		}

		if (progress.UnreadableFolders > 0)
		{
			notes.Add(EntryVisuals.Count(progress.UnreadableFolders, "folder", "folders") + " could not be read.");
		}

		return string.Join(' ', notes);
	}

	/// <summary>True when the walk saw everything below its root.</summary>
	public static bool IsComplete(RemoteSearchProgress progress)
	{
		ArgumentNullException.ThrowIfNull(progress);
		return progress.Limit == RemoteSearchLimit.None && progress.UnreadableFolders == 0;
	}
}
