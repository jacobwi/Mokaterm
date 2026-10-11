namespace Mokaterm.UI.Common.Platform;

/// <summary>
/// Session tools in native windows of their own. Only the desktop host registers it: in a browser a second window
/// would be a second circuit with a vault of its own, so the web host keeps tools in dialogs. The shell decides when
/// a window opens and when it has to go (its session closed, the vault locked); the host only manages windows.
/// </summary>
public interface IToolWindows
{
	/// <summary>Raised when a window opens or closes. May fire on any thread.</summary>
	event Action? Changed;

	/// <summary>Whether a window shows <paramref name="toolKey"/> for <paramref name="sessionId"/>.</summary>
	bool IsOpen(Guid sessionId, string toolKey);

	/// <summary>
	/// Opens the tool in a window, or brings the window already showing it to the front. False when no window could be
	/// opened, so the caller can show the tool another way.
	/// </summary>
	Task<bool> OpenAsync(ToolWindowRequest request);

	/// <summary>Closes the windows of one session, or only the one showing <paramref name="toolKey"/>.</summary>
	void Close(Guid sessionId, string? toolKey = null);

	/// <summary>Closes every window that borrows <paramref name="services"/>, such as when its vault locks.</summary>
	void CloseAll(IServiceProvider services);

	/// <summary>
	/// Brings the window <paramref name="services"/> belong to in front of its tool windows, for a question that window
	/// asks while the user works in one of them: it would otherwise wait unseen behind it. Does nothing while that
	/// window has no tool window open.
	/// </summary>
	void ShowOwner(IServiceProvider services);
}
