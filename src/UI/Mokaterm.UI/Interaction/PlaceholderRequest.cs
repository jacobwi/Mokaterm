namespace Mokaterm.UI.Interaction;

/// <summary>Asks for a command's placeholder values: one value per name, or null when cancelled.</summary>
internal sealed class PlaceholderRequest(PlaceholderPrompt prompt) : InteractionRequest<IReadOnlyDictionary<string, string>?>(null)
{
	public PlaceholderPrompt Prompt { get; } = prompt;
}
