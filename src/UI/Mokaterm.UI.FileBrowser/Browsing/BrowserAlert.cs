using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.UI.FileBrowser.Browsing;

/// <summary>An error shown above the list, optionally with a "Retry as root" action.</summary>
internal sealed class BrowserAlert
{
	private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

	public required string Title { get; init; }

	public required string Message { get; init; }

	/// <summary>Runs the failed work again against the root file system. Null when retrying as root is not offered.</summary>
	public Func<IRemoteFileSystem, Task>? RetryAsRoot { get; init; }

	/// <summary>
	/// Completes when the alert goes away: dismissed, replaced, retried or the browser closed. Uploads of dropped files
	/// wait for it so the browser keeps the dropped files readable while a retry is still possible.
	/// </summary>
	public Task Closed => _closed.Task;

	public void Close() => _closed.TrySetResult();
}
