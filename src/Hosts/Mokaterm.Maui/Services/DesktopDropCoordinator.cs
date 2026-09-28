using System.Collections.Concurrent;

namespace Mokaterm.Maui.Services;

/// <summary>
/// Pairs the two halves of a native file drop. The page script raises a Blazor call with a drop id, while WebView2
/// delivers the dropped paths for that id through its own message channel; either can arrive first.
/// </summary>
public sealed class DesktopDropCoordinator
{
	public const string MessagePrefix = "mokaterm-drop:";

	private readonly ConcurrentDictionary<string, TaskCompletionSource<IReadOnlyList<string>>> _pending = new(StringComparer.Ordinal);

	public void Complete(string dropId, IReadOnlyList<string> paths) => Get(dropId).TrySetResult(paths);

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
