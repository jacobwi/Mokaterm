using Mokaterm.Abstractions.Interaction;

namespace Mokaterm.Tests.Shared;

/// <summary>One call to <see cref="IUserInteraction.Notify"/>, as the recording fakes keep it.</summary>
internal sealed record Notice(NoticeSeverity Severity, string Message, string? Title);
