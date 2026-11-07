using Moka.Red.Feedback.Reconnect;

namespace Mokaterm.UI.Shell;

// What the Blazor Server hosts' reconnect screen says. A session lives in the server's circuit for this page, so the
// words say what each state means for the open sessions, which Moka's general wording cannot know.
public static class ReconnectWording
{
	public static MokaReconnectText Text { get; } = new()
	{
		ReconnectingMessage = "The connection to the server dropped. Your sessions keep running there while this page reconnects.",
		FailedMessage = "Check your connection, then try again. Your sessions stay open on the server for a few minutes, and a reload closes them.",
		PausedMessage = "The server set this page aside to save memory and closed its sessions. Resume to open the page again.",
		ResumeFailedMessage = "The server no longer has this page or its sessions. Reload to start again.",
	};
}
