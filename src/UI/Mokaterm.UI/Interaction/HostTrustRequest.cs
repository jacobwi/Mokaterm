using Mokaterm.Abstractions.Interaction;

namespace Mokaterm.UI.Interaction;

internal sealed class HostTrustRequest(HostTrustPrompt prompt) : InteractionRequest<HostTrustDecision>(HostTrustDecision.Reject)
{
	public HostTrustPrompt Prompt { get; } = prompt;
}
