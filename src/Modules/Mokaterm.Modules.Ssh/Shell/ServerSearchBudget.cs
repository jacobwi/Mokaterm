namespace Mokaterm.Modules.Ssh.Shell;

/// <summary>
/// What one search on the server may spend beyond the query's own caps. The script and the reader of its output take
/// the same budget, because the reader works out where the script stopped from it.
/// </summary>
/// <param name="TimeLimit">
/// How long find may run. The user's Stop ends it sooner; this is what ends it when closing the channel did not, or when
/// find writes nothing that would notice the channel closed.
/// </param>
/// <param name="MaxRecordBytes">The most bytes of records find may print, well inside the cap of every runner here.</param>
internal sealed record ServerSearchBudget(TimeSpan TimeLimit, int MaxRecordBytes)
{
	public static ServerSearchBudget Default { get; } = new(TimeSpan.FromMinutes(10), 24 * 1024 * 1024);
}
