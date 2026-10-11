namespace Mokaterm.UI.FileBrowser.Compare;

/// <summary>How two copies of a file are told apart.</summary>
internal sealed record FolderCompareOptions
{
	/// <summary>Servers keep times to the second and FAT drives to two seconds, so a closer pair counts as the same time.</summary>
	public static readonly TimeSpan DefaultTimeTolerance = TimeSpan.FromSeconds(2);

	public static FolderCompareOptions Default { get; } = new();

	public TimeSpan TimeTolerance { get; init; } = DefaultTimeTolerance;

	/// <summary>Compare sizes only, for a server whose times cannot be trusted (an FTP server listing in its own time zone).</summary>
	public bool IgnoreTimes { get; init; }

	/// <summary>Relative paths whose two copies hashed the same, which makes them the same whatever their times say.</summary>
	public IReadOnlySet<string>? SameContent { get; init; }
}
