namespace Mokaterm.Abstractions.ServerAdmin;

/// <summary>
/// What an <see cref="IHostMonitorFeature"/> has to show: the newest figures, how many reads failed since, and whether
/// this session can give figures at all.
/// </summary>
public sealed record HostMonitorState
{
	/// <summary>
	/// Reads in a row that may fail before the figures are too old to show. One failed read keeps them, marked stale; this
	/// many hides them until a read goes through again.
	/// </summary>
	public const int HideAfterFailures = 3;

	/// <summary>Nothing read yet.</summary>
	public static HostMonitorState Waiting { get; } = new();

	/// <summary>The figures of the last read that went through, or null before the first.</summary>
	public HostFigures? Figures { get; init; }

	/// <summary>Reads that failed since the last one that went through.</summary>
	public int FailedReads { get; init; }

	/// <summary>Why the last read failed, in words for the user; null when it went through.</summary>
	public string? Problem { get; init; }

	/// <summary>
	/// Why this session never gives figures (its login cannot run commands, its machine is not Linux), or null while it
	/// can. It is decided once and the reads stop for good.
	/// </summary>
	public string? Unavailable { get; init; }

	/// <summary>The figures are there but the read after them failed.</summary>
	public bool IsStale => FailedReads > 0;

	/// <summary>Whether there is anything worth showing: figures, not too many failures behind them, and a session that can read.</summary>
	public bool IsShown => Unavailable is null && Figures is not null && FailedReads < HideAfterFailures;
}
