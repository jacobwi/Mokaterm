using Mokaterm.Abstractions.Interaction;

namespace Mokaterm.UI.Interaction;

internal sealed class SecretRequest(SecretPrompt prompt) : InteractionRequest<SecretPromptResult?>(null)
{
	public SecretPrompt Prompt { get; } = prompt;
}
