using Mokaterm.Abstractions.Interaction;

namespace Mokaterm.UI.Interaction;

/// <summary>A notice waiting for <see cref="InteractionHost"/> to show it as a toast.</summary>
internal sealed record PendingNotice(NoticeSeverity Severity, string Message, string? Title);
