using Mokaterm.Abstractions.Sessions;

namespace Mokaterm.UI.Common.Components;

/// <summary>
/// What a <see cref="SessionViewBase"/> says while it has nothing to show. Every protocol's view is in the same place
/// when its session is not running, so it says it in the same words; only the line for a session that closed names
/// what the view would open.
/// </summary>
public static class SessionIdleText
{
	/// <param name="state">The session's state.</param>
	/// <param name="closed">
	/// The line for a session that is neither connecting nor failed, which names what this view opens, for example
	/// "Reconnect the session to open its screen again.".
	/// </param>
	public static string For(SessionState state, string closed) => state switch
	{
		SessionState.Connecting => "The session is still connecting.",
		SessionState.Failed => "The session failed. Reconnect it from the bar above.",
		_ => closed,
	};
}
