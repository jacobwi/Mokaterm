namespace Mokaterm.UI.Common.Platform;

/// <summary>
/// Tools in native windows of their own: session tools and app tools alike. Only the desktop host registers it: in a
/// browser a second window would be a second circuit with a vault of its own, so the web host keeps tools in dialogs.
/// The shell decides when a window opens and when it has to go (its session closed, the vault locked); the host only
/// manages windows.
/// </summary>
public interface IToolWindows
{
	/// <summary>Raised when a window opens or closes, or stops or starts staying on top. May fire on any thread.</summary>
	event Action? Changed;

	/// <summary>Whether a window shows <paramref name="target"/>.</summary>
	bool IsOpen(ToolWindowTarget target);

	/// <summary>
	/// Opens the tool in a window, or brings the window already showing it to the front. False when no window could be
	/// opened, so the caller can show the tool another way.
	/// </summary>
	Task<bool> OpenAsync(ToolWindowRequest request);

	/// <summary>Closes the window showing <paramref name="target"/>, if there is one.</summary>
	void Close(ToolWindowTarget target);

	/// <summary>Closes every window of one session's tools.</summary>
	void CloseSession(Guid sessionId);

	/// <summary>Closes every window that borrows <paramref name="services"/>, such as when its vault locks.</summary>
	void CloseAll(IServiceProvider services);

	/// <summary>
	/// Brings the window <paramref name="services"/> belong to in front of its tool windows, for a question that window
	/// asks while the user works in one of them: it would otherwise wait unseen behind it. Does nothing while that
	/// window has no tool window open.
	/// </summary>
	void ShowOwner(IServiceProvider services);

	/// <summary>Whether the window showing <paramref name="target"/> stays above other windows.</summary>
	bool IsKeptOnTop(ToolWindowTarget target);

	/// <summary>
	/// Keeps the window showing <paramref name="target"/> above other windows, or lets it go behind them again. The
	/// request's <see cref="ToolWindowRequest.BoundsChanged"/> hears of it, so the next window of that tool does the same.
	/// </summary>
	void SetKeepOnTop(ToolWindowTarget target, bool keepOnTop);
}
