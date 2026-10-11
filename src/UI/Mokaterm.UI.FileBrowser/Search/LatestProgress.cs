namespace Mokaterm.UI.FileBrowser.Search;

/// <summary>
/// Keeps the last report and nothing else. A walk reports once per folder, and a view that redraws on its own timer only
/// ever wants the newest one; <see cref="Progress{T}"/> would post every report to the renderer instead.
/// </summary>
internal sealed class LatestProgress<T> : IProgress<T>
	where T : class
{
	private T? _latest;

	public T? Latest => Volatile.Read(ref _latest);

	public void Report(T value) => Volatile.Write(ref _latest, value);
}
