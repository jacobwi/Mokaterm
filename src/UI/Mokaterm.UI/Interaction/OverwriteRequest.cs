using Mokaterm.Abstractions.Interaction;

namespace Mokaterm.UI.Interaction;

internal sealed class OverwriteRequest(OverwritePrompt prompt) : InteractionRequest<OverwriteDecision>(OverwriteDecision.Cancel)
{
	public OverwritePrompt Prompt { get; } = prompt;
}
