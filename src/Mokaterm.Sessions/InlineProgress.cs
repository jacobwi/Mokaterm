namespace Mokaterm.Sessions;

/// <summary>
/// Reports on the calling thread. <see cref="Progress{T}"/> posts to the context captured at construction,
/// which reorders reports on the thread pool and marshals them onto a Blazor renderer when created there.
/// </summary>
internal sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
	public void Report(T value) => report(value);
}
