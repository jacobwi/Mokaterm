using Moka.Red.Feedback.Toast;
using Mokaterm.Abstractions.Interaction;

namespace Mokaterm.UI.Interaction;

/// <summary>A notice waiting for <see cref="InteractionHost"/> to show it as a toast.</summary>
internal sealed record PendingNotice(NoticeSeverity Severity, string Message, string? Title)
{
	/// <summary>
	/// Whether the notification center keeps it after its toast has gone: a warning or an error asks for a look later,
	/// while a confirmation that something worked or a hint for the moment does not.
	/// </summary>
	public bool IsKept => Severity is NoticeSeverity.Warning or NoticeSeverity.Error;

	public MokaToastSeverity ToastSeverity => Severity switch
	{
		NoticeSeverity.Success => MokaToastSeverity.Success,
		NoticeSeverity.Warning => MokaToastSeverity.Warning,
		NoticeSeverity.Error => MokaToastSeverity.Error,
		_ => MokaToastSeverity.Info,
	};

	/// <summary>The notification center's entries need a title, and a notice may come without one.</summary>
	public string KeptTitle => Title ?? (Severity == NoticeSeverity.Error ? "Error" : "Warning");
}
