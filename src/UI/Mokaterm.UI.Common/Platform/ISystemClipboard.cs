namespace Mokaterm.UI.Common.Platform;

/// <summary>
/// The operating system's clipboard, reached without a page, for what no page can do: clear a copied secret once the
/// pages that could have cleared it are gone, the app's last window included. Only the desktop host registers one; a
/// browser's clipboard is out of the server's reach as soon as its page has closed.
/// </summary>
/// <remarks>
/// It never reads what the clipboard holds. Whether our copy is still there is told by the number the system gives the
/// clipboard's contents, recorded right after the copy: any write by any program moves it, and then the clipboard is
/// left alone, because what it holds is no longer ours to clear.
/// </remarks>
public interface ISystemClipboard
{
	/// <summary>The number of the clipboard's current contents, or null when this process may not read it.</summary>
	uint? ContentNumber { get; }

	/// <summary>
	/// Empties the clipboard while its number is still <paramref name="contentNumber"/>, checked with the clipboard held
	/// open so that no program can write between the check and the clear. Another program may hold the clipboard open
	/// for a moment, so this keeps trying for up to <paramref name="timeout"/>. Blocks the caller; never throws.
	/// </summary>
	SystemClipboardClear EmptyIfUnchanged(uint contentNumber, TimeSpan timeout);
}
