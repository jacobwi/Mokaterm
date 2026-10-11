namespace Mokaterm.Modules.Local.Containers;

/// <summary>What listing containers or pods for the editor came back with: the choices, or why there are none.</summary>
public sealed record ContainerChoiceList
{
	public IReadOnlyList<ContainerChoice> Choices { get; init; } = [];

	/// <summary>Why the list could not be read, in a sentence for the editor. Null when it was read.</summary>
	public string? Problem { get; init; }

	/// <summary>True when the tool printed more than is kept, so the list is the start of a longer one.</summary>
	public bool IsCut { get; init; }

	public static ContainerChoiceList Failed(string problem) => new() { Problem = problem };
}
