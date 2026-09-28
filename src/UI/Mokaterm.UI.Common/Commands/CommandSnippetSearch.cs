using Mokaterm.Abstractions.Commands;

namespace Mokaterm.UI.Common.Commands;

/// <summary>
/// Matching and labelling saved commands, shared by the session picker and the settings page so both search the
/// same way.
/// </summary>
public static class CommandSnippetSearch
{
	private static readonly char[] Separators = [' ', '\t'];

	/// <summary>
	/// The snippets whose name, command or tags contain every word of <paramref name="query"/>, in the order they
	/// came in. A blank query keeps everything.
	/// </summary>
	public static IReadOnlyList<CommandSnippet> Filter(IReadOnlyList<CommandSnippet> snippets, string? query)
	{
		ArgumentNullException.ThrowIfNull(snippets);
		string[] terms = Terms(query);
		return terms.Length == 0 ? snippets : [.. snippets.Where(snippet => Matches(snippet, terms))];
	}

	/// <summary>True when <paramref name="snippet"/> contains every word of <paramref name="query"/>.</summary>
	public static bool Matches(CommandSnippet snippet, string? query) => Matches(snippet, Terms(query));

	/// <summary>The scope as a short tag: Login, Machine or Global.</summary>
	public static string ScopeName(CommandSnippetScope scope) => scope switch
	{
		CommandSnippetScope.Connection => "Login",
		CommandSnippetScope.Host => "Machine",
		_ => "Global",
	};

	private static string[] Terms(string? query) =>
		string.IsNullOrWhiteSpace(query) ? [] : query.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

	private static bool Matches(CommandSnippet snippet, string[] terms)
	{
		ArgumentNullException.ThrowIfNull(snippet);
		foreach (string term in terms)
		{
			if (!Contains(snippet.Name, term) && !Contains(snippet.Command, term) && !snippet.Tags.Any(tag => Contains(tag, term)))
			{
				return false;
			}
		}

		return true;
	}

	private static bool Contains(string? value, string term) =>
		value is not null && value.Contains(term, StringComparison.CurrentCultureIgnoreCase);
}
