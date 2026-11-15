namespace Mokaterm.UI.Interaction;

/// <summary>
/// Hands the notices worth keeping to the main window's notification center (<see cref="NoticeHistory"/>). Window state: a
/// tool window's notice host shows its toasts where the tool is, and through this relay its warnings and errors still
/// reach the one bell.
/// </summary>
internal sealed class NoticeRelay
{
	/// <summary>Raised on the thread of the window that showed the notice.</summary>
	public event Action<PendingNotice>? Kept;

	public void Keep(PendingNotice notice)
	{
		ArgumentNullException.ThrowIfNull(notice);
		Kept?.Invoke(notice);
	}
}
