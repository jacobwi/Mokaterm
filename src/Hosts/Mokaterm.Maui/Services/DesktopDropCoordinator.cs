using System.Collections.Concurrent;

namespace Mokaterm.Maui.Services;

/// <summary>
/// Pairs the two halves of a native file drop. The window's drop target supplies the dropped paths under a drop id,
/// and the page raises a Blazor call with that id once it knows which drop zone the point was in; either can arrive
/// first.
/// </summary>
public sealed class DesktopDropCoordinator
{
	private readonly ConcurrentDictionary<string, TaskCompletionSource<IReadOnlyList<string>>> _pending = new(StringComparer.Ordinal);

	public void Complete(string dropId, IReadOnlyList<string> paths) => Get(dropId).TrySetResult(paths);

	/// <summary>
	/// Lets go of paths nobody claimed. A drop that landed outside every zone has no upload to hand them to, and
	/// without this its id would hold them for as long as the app runs.
	/// </summary>
	public void Forget(string dropId) => _pending.TryRemove(dropId, out _);

	public async Task<IReadOnlyList<string>> WaitForPathsAsync(string dropId, TimeSpan timeout, CancellationToken cancellationToken)
	{
		try
		{
			return await Get(dropId).Task.WaitAsync(timeout, cancellationToken);
		}
		catch (TimeoutException)
		{
			return [];
		}
		finally
		{
			_pending.TryRemove(dropId, out _);
		}
	}

	private TaskCompletionSource<IReadOnlyList<string>> Get(string dropId) =>
		_pending.GetOrAdd(dropId, static _ => new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously));
}
