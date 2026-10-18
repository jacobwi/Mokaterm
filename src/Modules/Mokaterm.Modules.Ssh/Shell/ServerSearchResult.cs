using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.Modules.Ssh.Shell;

/// <summary>What <see cref="RemoteScripts.Search"/> printed, read the way the folder by folder walk would have reported it.</summary>
internal sealed class ServerSearchResult
{
	/// <summary>Folders entered and entries found, in find's order. Entries already passed the query and its result cap.</summary>
	public required IReadOnlyList<ServerSearchStep> Steps { get; init; }

	/// <summary>Folders entered, the search's own folder included, as <see cref="RemoteSearchProgress.FoldersVisited"/> counts them.</summary>
	public required int FoldersVisited { get; init; }

	public required int UnreadableFolders { get; init; }

	public required RemoteSearchLimit Limit { get; init; }

	/// <summary>
	/// The server stopped at its byte cap before any cap of the query was reached, which only a pattern find could not say
	/// exactly lets happen: the walk has to answer this one instead.
	/// </summary>
	public bool NeedsWalk { get; init; }

	/// <summary>find ran out of the time its budget gives it; the steps are what it found until then.</summary>
	public bool TimedOut { get; init; }
}
