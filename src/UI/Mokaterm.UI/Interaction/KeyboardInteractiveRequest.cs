using Mokaterm.Abstractions.Interaction;

namespace Mokaterm.UI.Interaction;

internal sealed class KeyboardInteractiveRequest(KeyboardInteractivePrompt prompt) : InteractionRequest<IReadOnlyList<string>?>(null)
{
	public KeyboardInteractivePrompt Prompt { get; } = prompt;
}
