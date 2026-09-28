using Mokaterm.Abstractions.Commands;

namespace Mokaterm.Core.Commands;

/// <summary>The encrypted <c>commands</c> vault document.</summary>
internal sealed record CommandSnippetsDocument
{
	public const string DocumentName = "commands";

	public int Version { get; init; } = 1;

	public IReadOnlyList<CommandSnippet> Snippets { get; init; } = [];
}
