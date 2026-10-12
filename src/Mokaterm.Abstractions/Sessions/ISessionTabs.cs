namespace Mokaterm.Abstractions.Sessions;

/// <summary>
/// The shell's tabs, for a module that opened a session through <see cref="ISessionManager"/> for the user to work in
/// right away. A session the manager opens joins the pane the user is working in but leaves that pane's tab where it
/// was, so a module asks here to bring its session to the front. Registered by the shell.
/// </summary>
public interface ISessionTabs
{
	/// <summary>
	/// Makes the session the active tab of its pane and brings the window it is in to the front, which is what a tool
	/// shown in a window of its own needs. Works for a session whose open has not reached the tabs yet, and does nothing
	/// for one that never does.
	/// </summary>
	void Show(Guid sessionId);
}
