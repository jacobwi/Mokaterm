using Mokaterm.Abstractions.Interaction;

namespace Mokaterm.UI.Interaction;

internal sealed class CredentialsRequest(CredentialPrompt prompt) : InteractionRequest<CredentialPromptResult?>(null)
{
	public CredentialPrompt Prompt { get; } = prompt;
}
