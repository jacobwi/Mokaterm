namespace Mokaterm.UI.Common.Platform;

/// <summary>A page that is still open and can clear the clipboard for <see cref="ClipboardClearSchedule"/>.</summary>
internal interface IClipboardClearer
{
	/// <summary>
	/// Clears the clipboard for copy number <paramref name="copy"/>, or as soon as the page is focused again when it is in
	/// the background. A page that has already heard of a newer copy leaves the clipboard alone.
	/// </summary>
	/// <returns>
	/// False while the page holds the clear until it is focused again, so the secret is still on the clipboard; true once
	/// nothing of this copy is left for the page to do.
	/// </returns>
	ValueTask<bool> ClearAsync(long copy, CancellationToken cancellationToken);

	/// <summary>
	/// Says that copy number <paramref name="copy"/> replaced the clipboard, so a clear for an older copy that is still
	/// waiting for focus, or still on its way to the page, must not run.
	/// </summary>
	ValueTask ForgetClearsBeforeAsync(long copy, CancellationToken cancellationToken);
}
