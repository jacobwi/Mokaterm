using Microsoft.AspNetCore.Components;
using Mokaterm.Abstractions.Platform;

namespace Mokaterm.UI.Common.Platform;

/// <summary>Files and folders dropped from the operating system, with the modifier keys held at drop time.</summary>
public sealed record FileDropEvent(IReadOnlyList<LocalFileItem> Items, bool CtrlKey, bool ShiftKey, bool AltKey);

public sealed class FileDropHandlers
{
	/// <summary>
	/// Handles a drop. A bridge may release the dropped files once this task completes (the browser bridge does), so
	/// await the uploads that read them before returning.
	/// </summary>
	public required Func<FileDropEvent, Task> OnDrop { get; init; }

	/// <summary>True while OS files hover over the element, false when they leave or drop.</summary>
	public Func<bool, Task>? OnDragOver { get; init; }
}

/// <summary>
/// Turns operating-system file drops on an element into <see cref="LocalFileItem"/> lists, folders included.
/// The web host streams bytes from the browser; the desktop host can read dropped paths from disk. Drags that
/// start inside the app (moving remote entries) are not reported here.
/// </summary>
public interface IFileDropBridge
{
	/// <summary>Starts listening on <paramref name="element"/>. Dispose the result to stop.</summary>
	ValueTask<IAsyncDisposable> AttachAsync(ElementReference element, FileDropHandlers handlers, CancellationToken cancellationToken = default);
}
