namespace Mokaterm.Abstractions.FileSystem;

/// <summary>Where a search is. Reported as each folder is read, and once more when the walk ends.</summary>
public sealed record RemoteSearchProgress
{
	/// <summary>Folders read or tried so far, the one being read included.</summary>
	public int FoldersVisited { get; init; }

	/// <summary>The folder being read. Null in the report that ends the walk.</summary>
	public string? CurrentFolder { get; init; }

	/// <summary>Folders that could not be listed (permission denied, or gone): what they hold is missing from the results.</summary>
	public int UnreadableFolders { get; init; }

	/// <summary>Entries found so far.</summary>
	public int Found { get; init; }

	/// <summary>The cap that cut the walk short. Only the report that ends the walk says.</summary>
	public RemoteSearchLimit Limit { get; init; }
}
