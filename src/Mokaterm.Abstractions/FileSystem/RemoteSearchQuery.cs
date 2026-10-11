namespace Mokaterm.Abstractions.FileSystem;

/// <summary>What <see cref="IRemoteFileSystem.SearchAsync"/> looks for, and how far it may go to find it.</summary>
public sealed record RemoteSearchQuery
{
	public const int DefaultMaxResults = 1_000;

	public const int DefaultMaxFolders = 20_000;

	public const int DefaultMaxDepth = 32;

	/// <summary>
	/// A glob over the whole name when it holds <c>*</c> or <c>?</c> (<c>*.log</c> finds <c>error.log</c> and not
	/// <c>error.log.1</c>), otherwise a piece of the name (<c>access</c> finds <c>access.log.1</c>). Empty matches every name.
	/// </summary>
	public required string Pattern { get; init; }

	/// <summary>Off by default: a search box is usually typed in lower case.</summary>
	public bool MatchCase { get; init; }

	/// <summary>Also returns, and looks inside, entries whose names start with a dot.</summary>
	public bool IncludeHidden { get; init; }

	public RemoteSearchKinds Kinds { get; init; } = RemoteSearchKinds.All;

	/// <summary>The walk stops once it found this many.</summary>
	public int MaxResults { get; init; } = DefaultMaxResults;

	/// <summary>The most folders listed, the root included. Each listing is a round trip to the server.</summary>
	public int MaxFolders { get; init; } = DefaultMaxFolders;

	/// <summary>How many levels are searched: 1 is the root's own entries, 2 adds what its folders hold, and so on.</summary>
	public int MaxDepth { get; init; } = DefaultMaxDepth;

	/// <summary>True when <see cref="Pattern"/> is a glob rather than a piece of a name.</summary>
	public bool IsGlob => Pattern.AsSpan().IndexOfAny('*', '?') >= 0;

	/// <summary>True for an entry the search returns: hidden only when <see cref="IncludeHidden"/>, of one of the <see cref="Kinds"/>, and named to match.</summary>
	public bool Matches(RemoteFileEntry entry)
	{
		ArgumentNullException.ThrowIfNull(entry);
		RemoteSearchKinds kind = entry.IsDirectoryLike ? RemoteSearchKinds.Folders : RemoteSearchKinds.Files;
		return (IncludeHidden || !entry.IsHidden) && (Kinds & kind) != 0 && MatchesName(entry.Name);
	}

	/// <summary>True when <paramref name="name"/> matches <see cref="Pattern"/>.</summary>
	public bool MatchesName(string name)
	{
		ArgumentNullException.ThrowIfNull(name);
		if (Pattern.Length == 0)
		{
			return true;
		}

		return IsGlob
			? NameGlob.IsMatch(Pattern, name, ignoreCase: !MatchCase)
			: name.Contains(Pattern, MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
	}
}
