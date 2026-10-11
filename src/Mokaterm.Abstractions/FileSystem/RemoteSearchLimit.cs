namespace Mokaterm.Abstractions.FileSystem;

/// <summary>Which cap of a <see cref="RemoteSearchQuery"/> cut a search short.</summary>
public enum RemoteSearchLimit
{
	/// <summary>The search saw everything it was allowed into.</summary>
	None,

	/// <summary><see cref="RemoteSearchQuery.MaxResults"/> was reached while there was still more to look at.</summary>
	Results,

	/// <summary><see cref="RemoteSearchQuery.MaxFolders"/> were listed and more were waiting.</summary>
	Folders,

	/// <summary>Folders below <see cref="RemoteSearchQuery.MaxDepth"/> were left out; everything above was read.</summary>
	Depth,
}
