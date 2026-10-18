namespace Mokaterm.UI.Common.Platform;

public interface IClipboardService
{
	ValueTask WriteTextAsync(string text, CancellationToken cancellationToken = default);

	/// <summary>Null when the clipboard is empty or reading is not permitted.</summary>
	ValueTask<string?> ReadTextAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Copies a secret and clears the clipboard after <c>SecuritySettings.ClipboardClearSeconds</c>, through this page or,
	/// when it closed first, another one open over the same window state (the main window or one of its tool windows). A
	/// page in the background may not write to the clipboard, so a clear that finds it there happens when it is focused
	/// again, or at once through <see cref="ISystemClipboard"/> where the host has one, which also clears a secret still
	/// due when the app shuts down. A plain copy made meanwhile cancels the clear.
	/// </summary>
	ValueTask WriteSecretAsync(string secret, CancellationToken cancellationToken = default);
}
