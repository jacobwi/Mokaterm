namespace Mokaterm.UI.Common.Platform;

public interface IClipboardService
{
	ValueTask WriteTextAsync(string text, CancellationToken cancellationToken = default);

	/// <summary>Null when the clipboard is empty or reading is not permitted.</summary>
	ValueTask<string?> ReadTextAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Copies a secret and clears the clipboard after <c>SecuritySettings.ClipboardClearSeconds</c>. A page in the
	/// background may not write to the clipboard, so a clear that finds it there happens when it is focused again.
	/// </summary>
	ValueTask WriteSecretAsync(string secret, CancellationToken cancellationToken = default);
}
