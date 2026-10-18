using Mokaterm.Abstractions.Commands;

namespace Mokaterm.UI.Interaction;

/// <summary>
/// The question a command with placeholders asks before it is typed or run: one field per name, started from
/// <see cref="Values"/>. Public because it is a component parameter, and a class rather than a record so nothing prints
/// the values, which can be secrets.
/// </summary>
public sealed class PlaceholderPrompt
{
	public required string Title { get; init; }

	public required CommandTemplate Template { get; init; }

	/// <summary>What each field starts with, by name: the last answer for this command, else its default.</summary>
	public IReadOnlyDictionary<string, string> Values { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

	/// <summary>The primary button, which says what happens next: Type, Run, Run on 3 logins.</summary>
	public string ConfirmText { get; init; } = "OK";
}
