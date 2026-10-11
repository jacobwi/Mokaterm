namespace Mokaterm.UI.Workspace;

/// <summary>
/// Asks a session's view to press one of its toolbar buttons, which is how the command palette reaches buttons such as
/// Find or Clear that only the view can carry out: it holds the terminal. A button the user hid from the toolbar still
/// works this way.
/// </summary>
internal sealed class SessionViewRequests
{
	/// <summary>The session and the button id from <c>SessionToolbarButtons</c>. Raised on the caller's thread.</summary>
	public event Action<Guid, string>? Requested;

	public void Request(Guid sessionId, string buttonId)
	{
		ArgumentException.ThrowIfNullOrEmpty(buttonId);
		Requested?.Invoke(sessionId, buttonId);
	}
}
