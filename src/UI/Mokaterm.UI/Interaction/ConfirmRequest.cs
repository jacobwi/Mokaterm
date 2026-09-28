using Mokaterm.Abstractions.Interaction;

namespace Mokaterm.UI.Interaction;

internal sealed class ConfirmRequest(ConfirmPrompt prompt) : InteractionRequest<ConfirmResult>(ConfirmResult.No)
{
	public ConfirmPrompt Prompt { get; } = prompt;
}
